using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Serilog;

namespace Kiriha.Core.Mpv;

internal static class MpvNodeParser
{
    internal static List<TrackInfo> ParseTracks(MpvNode root)
    {
        var tracks = new List<TrackInfo>();
        if (!TryGetNodeList(root, LibMpvNative.MPV_FORMAT_NODE_ARRAY, out var list))
            return tracks;

        for (int i = 0; i < list.Num; i++)
        {
            var trackNode = ReadNode(list.Values, i);
            if (trackNode.Format != LibMpvNative.MPV_FORMAT_NODE_MAP)
                continue;

            var type = GetMapString(trackNode, "type");
            var id = GetMapString(trackNode, "id");
            if (type == null || id == null)
                continue;

            tracks.Add(new TrackInfo
            {
                Type = type,
                Id = id,
                Title = GetMapString(trackNode, "title"),
                Lang = GetMapString(trackNode, "lang"),
                Selected = GetMapBool(trackNode, "selected")
            });
        }

        return tracks;
    }

    internal static List<ChapterInfo> ParseChapters(MpvNode root)
    {
        var chapters = new List<ChapterInfo>();
        if (!TryGetNodeList(root, LibMpvNative.MPV_FORMAT_NODE_ARRAY, out var list))
            return chapters;

        for (int i = 0; i < list.Num; i++)
        {
            var chapterNode = ReadNode(list.Values, i);
            if (chapterNode.Format != LibMpvNative.MPV_FORMAT_NODE_MAP)
                continue;

            chapters.Add(new ChapterInfo
            {
                Title = GetMapString(chapterNode, "title") ?? $"Chapter {i + 1}",
                Time = GetMapDouble(chapterNode, "time") ?? 0
            });
        }

        return chapters;
    }

    private static bool TryGetNodeList(MpvNode node, int expectedFormat, out MpvNodeList list)
    {
        if (node.Format != expectedFormat || node.U.List == IntPtr.Zero)
        {
            list = default;
            return false;
        }

        list = Marshal.PtrToStructure<MpvNodeList>(node.U.List);
        return list.Num > 0 && list.Values != IntPtr.Zero;
    }

    private static MpvNode ReadNode(IntPtr values, int index)
    {
        return Marshal.PtrToStructure<MpvNode>(IntPtr.Add(values, index * Marshal.SizeOf<MpvNode>()));
    }

    private static bool TryGetMapValue(MpvNode mapNode, string key, out MpvNode value)
    {
        value = default;
        if (!TryGetNodeList(mapNode, LibMpvNative.MPV_FORMAT_NODE_MAP, out var list) || list.Keys == IntPtr.Zero)
            return false;

        for (int i = 0; i < list.Num; i++)
        {
            var keyPtr = Marshal.ReadIntPtr(list.Keys, i * IntPtr.Size);
            var currentKey = keyPtr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(keyPtr);
            if (!string.Equals(currentKey, key, StringComparison.Ordinal))
                continue;

            value = ReadNode(list.Values, i);
            return true;
        }

        return false;
    }

    private static string? GetMapString(MpvNode mapNode, string key)
    {
        if (!TryGetMapValue(mapNode, key, out var value))
            return null;

        return value.Format switch
        {
            LibMpvNative.MPV_FORMAT_STRING when value.U.String != IntPtr.Zero => Marshal.PtrToStringUTF8(value.U.String),
            LibMpvNative.MPV_FORMAT_INT64 => value.U.Int64.ToString(System.Globalization.CultureInfo.InvariantCulture),
            LibMpvNative.MPV_FORMAT_DOUBLE => value.U.Double.ToString(System.Globalization.CultureInfo.InvariantCulture),
            LibMpvNative.MPV_FORMAT_FLAG => value.U.Flag != 0 ? "yes" : "no",
            _ => null
        };
    }

    private static double? GetMapDouble(MpvNode mapNode, string key)
    {
        if (!TryGetMapValue(mapNode, key, out var value))
            return null;

        return value.Format switch
        {
            LibMpvNative.MPV_FORMAT_DOUBLE => value.U.Double,
            LibMpvNative.MPV_FORMAT_INT64 => value.U.Int64,
            _ => null
        };
    }

    private static bool GetMapBool(MpvNode mapNode, string key)
    {
        if (!TryGetMapValue(mapNode, key, out var value))
            return false;

        return value.Format switch
        {
            LibMpvNative.MPV_FORMAT_FLAG => value.U.Flag != 0,
            LibMpvNative.MPV_FORMAT_STRING when value.U.String != IntPtr.Zero =>
                string.Equals(Marshal.PtrToStringUTF8(value.U.String), "yes", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }
}
