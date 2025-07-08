using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;
using Xilium.CefGlue;

namespace CST.Avalonia.Services
{
    /// <summary>
    /// Page reference data structure for JavaScript-to-C# communication
    /// </summary>
    public class PageReferences
    {
        public string Vri { get; set; } = "*";
        public string Myanmar { get; set; } = "*";
        public string Pts { get; set; } = "*";
        public string Thai { get; set; } = "*";
        public string Other { get; set; } = "*";
    }

    /// <summary>
    /// Custom scheme handler factory for serving HTML content directly without URL limitations
    /// </summary>
    public class CstSchemeHandlerFactory : CefSchemeHandlerFactory
    {
        public const string SchemeName = "http";
        public const string DomainName = "cst-local";
        
        // Logging service for both console and file output
        private static readonly LoggingService _logger = LoggingService.Instance;
        
        // Thread-safe storage for HTML content
        private static readonly ConcurrentDictionary<string, string> _contentStore = new();
        
        // Note: Static events removed to prevent cross-tab interference
        
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
            _logger.LogDebug("CstSchemeHandlerFactory", "Create called");
            _logger.LogDebug("CstSchemeHandlerFactory", "Scheme", schemeName);
            _logger.LogDebug("CstSchemeHandlerFactory", "URL", request.Url);
            _logger.LogDebug("CstSchemeHandlerFactory", "Method", request.Method);
            
            // Extract content ID from URL path
            var uri = new Uri(request.Url);
            var path = uri.AbsolutePath.TrimStart('/');
            
            // Handle page reference callback
            if (path == "page-references" && request.Method == "POST")
            {
                _logger.LogDebug("CstSchemeHandlerFactory", "Handling page reference callback");
                return new CstPageReferenceHandler();
            }
            
            var contentId = path.Replace(".html", "");
            
            _logger.LogDebug("CstSchemeHandlerFactory", "Extracted content ID", contentId);
            _logger.LogDebug("CstSchemeHandlerFactory", "Available content IDs", $"[{string.Join(", ", _contentStore.Keys)}]");
            
            // Retrieve stored content
            if (_contentStore.TryGetValue(contentId, out var htmlContent))
            {
                _logger.LogDebug("CstSchemeHandlerFactory", "Found content", $"'{contentId}' - length: {htmlContent.Length}");
                _logger.LogDebug("CstSchemeHandlerFactory", "Content preview", htmlContent.Substring(0, Math.Min(100, htmlContent.Length)) + "...");
                return new CstResourceHandler(htmlContent);
            }
            
            // Return 404 if content not found
            _logger.LogWarning("CstSchemeHandlerFactory", "Content NOT found - returning 404", contentId);
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
        private static readonly LoggingService _logger = LoggingService.Instance;

        public CstResourceHandler(string htmlContent, int statusCode = 200)
        {
            _data = Encoding.UTF8.GetBytes(htmlContent);
            _statusCode = statusCode;
            _offset = 0;
            _isOpen = false;
        }

        protected override bool Open(CefRequest request, out bool handleRequest, CefCallback callback)
        {
            _logger.LogDebug("CstResourceHandler", "Open called", $"data length: {_data.Length}, status: {_statusCode}");
            
            // Signal that we'll handle the request
            handleRequest = true;
            _isOpen = true;
            
            // Call the callback immediately since we have the data ready
            callback.Continue();
            
            return true;
        }

        protected override void GetResponseHeaders(CefResponse response, out long responseLength, out string redirectUrl)
        {
            _logger.LogDebug("CstResourceHandler", "GetResponseHeaders called", $"setting up response for {_data.Length} bytes");
            
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
            
            _logger.LogDebug("CstResourceHandler", "Response headers set", $"length: {responseLength}, status: {_statusCode}");
        }

        protected override bool Read(Stream response, int bytesToRead, out int bytesRead, CefResourceReadCallback callback)
        {
            if (!_isOpen || _offset >= _data.Length)
            {
                _logger.LogDebug("CstResourceHandler", "Read - end of data", $"open: {_isOpen}, offset: {_offset}, data length: {_data.Length}");
                bytesRead = 0;
                return false;
            }

            bytesRead = Math.Min(bytesToRead, _data.Length - _offset);
            response.Write(_data, _offset, bytesRead);
            _offset += bytesRead;
            
            _logger.LogDebug("CstResourceHandler", "Read - sent bytes", $"{bytesRead} bytes (offset now: {_offset}/{_data.Length})");
            
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

    /// <summary>
    /// Resource handler for page reference callbacks from JavaScript
    /// </summary>
    public class CstPageReferenceHandler : CefResourceHandler
    {
        private readonly byte[] _responseData;
        private int _offset;
        private bool _isOpen;
        private static readonly LoggingService _logger = LoggingService.Instance;

        public CstPageReferenceHandler()
        {
            _responseData = Encoding.UTF8.GetBytes("{\"status\":\"ok\"}");
            _offset = 0;
            _isOpen = false;
        }

        protected override bool Open(CefRequest request, out bool handleRequest, CefCallback callback)
        {
            _logger.LogDebug("CstPageReferenceHandler", "Open called");
            
            handleRequest = true;
            _isOpen = true;
            
            // Read POST data asynchronously
            var postData = request.PostData;
            if (postData != null)
            {
                var elements = postData.GetElements();
                if (elements.Length > 0)
                {
                    var element = elements[0];
                    var data = element.GetBytes();
                    if (data != null)
                    {
                        var jsonString = Encoding.UTF8.GetString(data);
                        _logger.LogDebug("CstPageReferenceHandler", "Received page reference data", jsonString);
                        
                        try
                        {
                            var pageReferences = JsonSerializer.Deserialize<PageReferences>(jsonString);
                            if (pageReferences != null)
                            {
                                _logger.LogDebug("CstPageReferenceHandler", "Parsed page references", $"VRI={pageReferences.Vri}, Myanmar={pageReferences.Myanmar}, PTS={pageReferences.Pts}, Thai={pageReferences.Thai}, Other={pageReferences.Other}");
                                
                                // Fire the event to notify subscribers
                                // Note: Static event triggering removed to prevent cross-tab interference
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError("CstPageReferenceHandler", "Error parsing page reference JSON", ex.Message);
                        }
                    }
                }
            }
            
            callback.Continue();
            return true;
        }

        protected override void GetResponseHeaders(CefResponse response, out long responseLength, out string redirectUrl)
        {
            response.Status = 200;
            response.StatusText = "OK";
            response.MimeType = "application/json";
            response.Charset = "utf-8";
            
            response.SetHeaderByName("Content-Type", "application/json; charset=utf-8", false);
            response.SetHeaderByName("Access-Control-Allow-Origin", "*", false);
            response.SetHeaderByName("Access-Control-Allow-Methods", "POST", false);
            response.SetHeaderByName("Access-Control-Allow-Headers", "Content-Type", false);
            
            responseLength = _responseData.Length;
            redirectUrl = "";
        }

        protected override bool Read(Stream response, int bytesToRead, out int bytesRead, CefResourceReadCallback callback)
        {
            if (!_isOpen || _offset >= _responseData.Length)
            {
                bytesRead = 0;
                return false;
            }

            bytesRead = Math.Min(bytesToRead, _responseData.Length - _offset);
            response.Write(_responseData, _offset, bytesRead);
            _offset += bytesRead;
            
            callback.Continue(bytesRead);
            return bytesRead > 0;
        }

        protected override bool Skip(long bytesToSkip, out long bytesSkipped, CefResourceSkipCallback callback)
        {
            if (!_isOpen || _offset >= _responseData.Length)
            {
                bytesSkipped = 0;
                return false;
            }

            bytesSkipped = Math.Min(bytesToSkip, _responseData.Length - _offset);
            _offset += (int)bytesSkipped;
            
            callback.Continue(bytesSkipped);
            return bytesSkipped > 0;
        }

        protected override void Cancel()
        {
            _isOpen = false;
        }

        [Obsolete]
        protected override bool ProcessRequest(CefRequest request, CefCallback callback)
        {
            callback.Continue();
            return true;
        }

        [Obsolete]
        protected override bool ReadResponse(Stream response, int bytesToRead, out int bytesRead, CefCallback callback)
        {
            if (_offset >= _responseData.Length)
            {
                bytesRead = 0;
                return false;
            }

            bytesRead = Math.Min(bytesToRead, _responseData.Length - _offset);
            response.Write(_responseData, _offset, bytesRead);
            _offset += bytesRead;
            
            return true;
        }
    }
}