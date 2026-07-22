using Kiriha.Utils;
using Kiriha.Utils.Parsing;
using Kiriha.Utils.Collections;
using Kiriha.Utils.Async;
using Kiriha.Utils.Graphs;
using Kiriha.Utils.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Kiriha.Models;
using Serilog;
using System.IO;

namespace Kiriha.Services.Tracking.Anisthesia.Strategies;

public class HandleEnumerationStrategy
{
    // P/Invoke constants and structures
    private const int SystemExtendedHandleInformation = 64;
    private const int STATUS_INFO_LENGTH_MISMATCH = unchecked((int)0xC0000004);
    private const int STATUS_SUCCESS = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX
    {
        public IntPtr Object;
        public IntPtr UniqueProcessId;
        public IntPtr HandleValue;
        public uint GrantedAccess;
        public ushort CreatorBackTraceIndex;
        public ushort ObjectTypeIndex;
        public uint HandleAttributes;
        public uint Reserved;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int SystemInformationClass, IntPtr SystemInformation, int SystemInformationLength, out int ReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateHandle(IntPtr hSourceProcessHandle, IntPtr hSourceHandle, IntPtr hTargetProcessHandle, out IntPtr lpTargetHandle, uint dwDesiredAccess, bool bInheritHandle, uint dwOptions);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetFinalPathNameByHandle(IntPtr hFile, [Out] char[] lpszFilePath, uint cchFilePath, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetFileType(IntPtr hFile);

    private const uint PROCESS_DUP_HANDLE = 0x0040;
    private const uint DUPLICATE_SAME_ACCESS = 0x00000002;
    private const uint FILE_TYPE_DISK = 0x0001;
    private const uint FILE_NAME_NORMALIZED = 0x0;
    private const uint VOLUME_NAME_DOS = 0x0;

    public static unsafe List<string> GetOpenFiles(uint pid)
    {
        var files = new List<string>();
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IntPtr processHandle = OpenProcess(PROCESS_DUP_HANDLE, false, pid);
        if (processHandle == IntPtr.Zero) return files;

        try
        {
            int bufferSize = 1024 * 1024; // 1MB initial
            IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
            try
            {
                int returnLength;

                while (NtQuerySystemInformation(SystemExtendedHandleInformation, buffer, bufferSize, out returnLength) == STATUS_INFO_LENGTH_MISMATCH)
                {
                    bufferSize = returnLength;
                    Marshal.FreeHGlobal(buffer);
                    buffer = IntPtr.Zero;
                    buffer = Marshal.AllocHGlobal(bufferSize);
                }

                long handleCount = Marshal.ReadInt64(buffer);
                int entrySize = Marshal.SizeOf<SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX>();
                IntPtr currentPtr = buffer + 16; // Skip NumberOfHandles and Reserved (8+8 bytes)

                char[] pathBuffer = System.Buffers.ArrayPool<char>.Shared.Rent(32768);
                try
                {
                    for (long i = 0; i < handleCount; i++)
                    {
                        SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX* entry = (SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX*)currentPtr;
                        currentPtr += entrySize;

                        if ((uint)entry->UniqueProcessId != pid) continue;

                        // Simple access mask check (FILE_READ_DATA = 0x0001)
                        // Anisthesia uses more complex checks, but let's start with basic
                        if ((entry->GrantedAccess & 0x0001) == 0) continue;

                        if (DuplicateHandle(processHandle, entry->HandleValue, GetCurrentProcess(), out IntPtr dupHandle, 0, false, DUPLICATE_SAME_ACCESS))
                        {
                            try
                            {
                                if (GetFileType(dupHandle) == FILE_TYPE_DISK)
                                {
                                    uint pathLen = GetFinalPathNameByHandle(dupHandle, pathBuffer, (uint)pathBuffer.Length, 0);
                                    if (pathLen > 0 && pathLen < pathBuffer.Length)
                                    {
                                        string path;
                                        if (pathLen >= 4 && pathBuffer[0] == '\\' && pathBuffer[1] == '\\' && pathBuffer[2] == '?' && pathBuffer[3] == '\\')
                                        {
                                            path = new string(pathBuffer, 4, (int)pathLen - 4);
                                        }
                                        else
                                        {
                                            path = new string(pathBuffer, 0, (int)pathLen);
                                        }
                                        
                                        if (seenFiles.Add(path))
                                        {
                                            files.Add(path);
                                        }
                                    }
                                }
                            }
                            finally
                            {
                                CloseHandle(dupHandle);
                            }
                        }
                    }
                }
                finally
                {
                    System.Buffers.ArrayPool<char>.Shared.Return(pathBuffer);
                }
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error enumerating handles for PID {PID}", pid);
        }
        finally
        {
            CloseHandle(processHandle);
        }

        return files;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    public static ParsedMedia? Apply(AnisthesiaPlayer player, uint pid)
    {
        var files = GetOpenFiles(pid);
        // Video file extensions to filter
        var videoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".flv", ".ogm" };

        foreach (var file in files)
        {
            string ext = System.IO.Path.GetExtension(file);
            if (videoExtensions.Contains(ext))
            {
                string filename = System.IO.Path.GetFileName(file);
                
                // Use AnitomySharp to parse the filename
                var elements = Kiriha.Utils.Parsing.AnimeParseCache.Parse(filename);
                var titleElement = elements.FirstOrDefault(e => e.Category == AnitomySharp.Element.ElementCategory.ElementAnimeTitle);
                var subTitleElement = elements.FirstOrDefault(e => e.Category == AnitomySharp.Element.ElementCategory.ElementEpisodeTitle);
                var otherElement = elements.FirstOrDefault(e => e.Category == AnitomySharp.Element.ElementCategory.ElementOther);
                var episode = elements.FirstOrDefault(e => e.Category == AnitomySharp.Element.ElementCategory.ElementEpisodeNumber)?.Value;
                var season = elements.FirstOrDefault(e => e.Category == AnitomySharp.Element.ElementCategory.ElementAnimeSeason)?.Value;
                var group = elements.FirstOrDefault(e => e.Category == AnitomySharp.Element.ElementCategory.ElementReleaseGroup)?.Value;

                string animeTitle;
                bool isEmber = file.Contains("EMBER", StringComparison.OrdinalIgnoreCase) || EmberTitleResolver.ScanFileForEmber(file);

                if (titleElement != null && !isEmber)
                {
                    animeTitle = titleElement.Value;
                }
                else
                {
                    string meaningfulDir = EmberTitleResolver.GetMeaningfulDirectoryName(file);
                    if (!string.IsNullOrEmpty(meaningfulDir))
                    {
                        var dirElements = Kiriha.Utils.Parsing.AnimeParseCache.Parse(meaningfulDir);
                        var dirTitleElement = dirElements.FirstOrDefault(e => e.Category == AnitomySharp.Element.ElementCategory.ElementAnimeTitle);
                        animeTitle = dirTitleElement != null ? dirTitleElement.Value : meaningfulDir;
                    }
                    else
                    {
                        animeTitle = titleElement != null ? titleElement.Value : Path.GetFileNameWithoutExtension(filename);
                    }
                }

                string episodeTitle = string.Empty;

                if (subTitleElement != null)
                    episodeTitle = subTitleElement.Value;
                
                if (otherElement != null)
                    episodeTitle = string.IsNullOrEmpty(episodeTitle) ? otherElement.Value : $"{episodeTitle} {otherElement.Value}";

                return new ParsedMedia
                {
                    OriginalTitle = filename,
                    AnimeTitle = animeTitle,
                    EpisodeTitle = episodeTitle,
                    Episode = episode ?? string.Empty,
                    Season = season ?? string.Empty,
                    Group = group ?? string.Empty,
                    IsPlaying = true,
                    ProcessName = player.Executables.FirstOrDefault() ?? ""
                };
            }
        }

        return null;
    }
}
