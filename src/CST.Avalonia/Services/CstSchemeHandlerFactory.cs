using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using Xilium.CefGlue;

namespace CST.Avalonia.Services
{
    /// <summary>
    /// Custom scheme handler factory for serving HTML content directly without URL limitations
    /// </summary>
    public class CstSchemeHandlerFactory : CefSchemeHandlerFactory
    {
        public const string SchemeName = "http";
        public const string DomainName = "cst-local";
        
        // Thread-safe storage for HTML content
        private static readonly ConcurrentDictionary<string, string> _contentStore = new();
        
        /// <summary>
        /// Store HTML content and return the URL to access it
        /// </summary>
        public static string StoreHtmlContent(string htmlContent, string contentId = "content")
        {
            _contentStore[contentId] = htmlContent;
            return $"{SchemeName}://{DomainName}/{contentId}.html";
        }
        
        /// <summary>
        /// Clear stored content
        /// </summary>
        public static void ClearContent(string contentId = "content")
        {
            _contentStore.TryRemove(contentId, out _);
        }

        protected override CefResourceHandler Create(CefBrowser browser, CefFrame frame, string schemeName, CefRequest request)
        {
            Console.WriteLine($"CstSchemeHandlerFactory.Create called:");
            Console.WriteLine($"  Scheme: {schemeName}");
            Console.WriteLine($"  URL: {request.Url}");
            Console.WriteLine($"  Method: {request.Method}");
            
            // Extract content ID from URL path
            var uri = new Uri(request.Url);
            var path = uri.AbsolutePath.TrimStart('/');
            var contentId = path.Replace(".html", "");
            
            Console.WriteLine($"  Extracted content ID: '{contentId}'");
            Console.WriteLine($"  Available content IDs: [{string.Join(", ", _contentStore.Keys)}]");
            
            // Retrieve stored content
            if (_contentStore.TryGetValue(contentId, out var htmlContent))
            {
                Console.WriteLine($"  Found content for '{contentId}' - length: {htmlContent.Length}");
                Console.WriteLine($"  Content preview: {htmlContent.Substring(0, Math.Min(100, htmlContent.Length))}...");
                return new CstResourceHandler(htmlContent);
            }
            
            // Return 404 if content not found
            Console.WriteLine($"  Content NOT found for '{contentId}' - returning 404");
            return new CstResourceHandler("<html><body><h1>404 - Content Not Found</h1><p>Requested: " + contentId + "</p></body></html>", 404);
        }
    }
    
    /// <summary>
    /// Resource handler for serving HTML content
    /// </summary>
    public class CstResourceHandler : CefResourceHandler
    {
        private readonly byte[] _data;
        private readonly int _statusCode;
        private int _offset;
        private bool _isOpen;

        public CstResourceHandler(string htmlContent, int statusCode = 200)
        {
            _data = Encoding.UTF8.GetBytes(htmlContent);
            _statusCode = statusCode;
            _offset = 0;
            _isOpen = false;
        }

        protected override bool Open(CefRequest request, out bool handleRequest, CefCallback callback)
        {
            Console.WriteLine($"CstResourceHandler.Open called - data length: {_data.Length}, status: {_statusCode}");
            
            // Signal that we'll handle the request
            handleRequest = true;
            _isOpen = true;
            
            // Call the callback immediately since we have the data ready
            callback.Continue();
            
            return true;
        }

        protected override void GetResponseHeaders(CefResponse response, out long responseLength, out string redirectUrl)
        {
            Console.WriteLine($"CstResourceHandler.GetResponseHeaders called - setting up response for {_data.Length} bytes");
            
            response.Status = _statusCode;
            response.StatusText = _statusCode == 200 ? "OK" : "Not Found";
            response.MimeType = "text/html";
            response.Charset = "utf-8";
            
            // Set response headers - simplified approach
            response.SetHeaderByName("Content-Type", "text/html; charset=utf-8", false);
            response.SetHeaderByName("Access-Control-Allow-Origin", "*", false);
            response.SetHeaderByName("Cache-Control", "no-cache, no-store, must-revalidate", false);
            
            responseLength = _data.Length;
            redirectUrl = "";
            
            Console.WriteLine($"Response headers set - length: {responseLength}, status: {_statusCode}");
        }

        protected override bool Read(Stream response, int bytesToRead, out int bytesRead, CefResourceReadCallback callback)
        {
            if (!_isOpen || _offset >= _data.Length)
            {
                Console.WriteLine($"CstResourceHandler.Read - end of data (open: {_isOpen}, offset: {_offset}, data length: {_data.Length})");
                bytesRead = 0;
                return false;
            }

            bytesRead = Math.Min(bytesToRead, _data.Length - _offset);
            response.Write(_data, _offset, bytesRead);
            _offset += bytesRead;
            
            Console.WriteLine($"CstResourceHandler.Read - sent {bytesRead} bytes (offset now: {_offset}/{_data.Length})");
            
            // Call the callback to signal completion
            callback.Continue(bytesRead);
            
            return bytesRead > 0;
        }

        protected override bool Skip(long bytesToSkip, out long bytesSkipped, CefResourceSkipCallback callback)
        {
            if (!_isOpen || _offset >= _data.Length)
            {
                bytesSkipped = 0;
                return false;
            }

            bytesSkipped = Math.Min(bytesToSkip, _data.Length - _offset);
            _offset += (int)bytesSkipped;
            
            // Call the callback to signal completion
            callback.Continue(bytesSkipped);
            
            return bytesSkipped > 0;
        }

        protected override void Cancel()
        {
            _isOpen = false;
        }

        // Obsolete methods - still implement for compatibility
        [Obsolete]
        protected override bool ProcessRequest(CefRequest request, CefCallback callback)
        {
            // This is called for older CEF versions
            callback.Continue();
            return true;
        }

        [Obsolete]
        protected override bool ReadResponse(Stream response, int bytesToRead, out int bytesRead, CefCallback callback)
        {
            // This is called for older CEF versions
            if (_offset >= _data.Length)
            {
                bytesRead = 0;
                return false;
            }

            bytesRead = Math.Min(bytesToRead, _data.Length - _offset);
            response.Write(_data, _offset, bytesRead);
            _offset += bytesRead;
            
            return true;
        }
    }
}