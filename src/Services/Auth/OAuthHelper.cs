using System;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
using Serilog;

namespace Kiriha.Services.Auth;

public static class OAuthHelper
{
    public static async Task<string?> AuthorizeViaLoopbackAsync(string authUrl, string redirectUri, string successMessage)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();

        Log.Information("Opening browser for authorization...");
        Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        
        try 
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var context = await listener.GetContextAsync().WaitAsync(cts.Token);
                var request = context.Request;
                
                // Ignore favicon and other irrelevant requests
                if (request.Url?.AbsolutePath == "/favicon.ico")
                {
                    context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                    context.Response.Close();
                    continue;
                }

                var code = request.QueryString["code"];
                if (string.IsNullOrEmpty(code))
                {
                    // If no code, but it's not a favicon, maybe it's an error from the provider?
                    var error = request.QueryString["error"];
                    if (!string.IsNullOrEmpty(error))
                    {
                        Log.Error("OAuth error returned from provider: {Error}", error);
                        context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                        context.Response.Close();
                        return null;
                    }
                    
                    // Otherwise keep waiting
                    continue;
                }

                // We got the code!
                using var response = context.Response;
                response.ContentType = "text/html; charset=utf-8";
                string localizedCloseMsg = UIUtils.GetLoc("auth.close_window");
                var responseString = $"<html><head><meta charset='utf-8'></head><body><h1 style='font-family:sans-serif;'>{successMessage}</h1><p style='font-family:sans-serif;'>{localizedCloseMsg}</p></body></html>";
                var buffer = Encoding.UTF8.GetBytes(responseString);
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                await response.OutputStream.FlushAsync();
                
                // Brief delay to ensure browser receives response
                await Task.Delay(500);
                
                listener.Stop();
                return code;
            }
        }
        catch (OperationCanceledException)
        {
            Log.Warning("Authorization timed out.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception during loopback authorization");
        }
        finally
        {
            try { listener.Stop(); } catch (Exception ex) { Log.Debug(ex, "Failed to stop HttpListener"); }
        }

        return null;
    }
}
