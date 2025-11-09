using System.Xml;
using System.Xml.Xsl;
using CST;

namespace CST.MAUI.Services;

public class BookService
{
    private readonly string _xmlBooksPath;
    private XslCompiledTransform? _cachedXslTransform;
    private readonly object _xslLock = new object();

    public BookService()
    {
        // Use the shared CSTReader Application Support directory
        // On macOS: /Users/{username}/Library/Application Support/CSTReader/xml
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _xmlBooksPath = Path.Combine(
            homeDir,
            "Library",
            "Application Support",
            "CSTReader",
            "xml"
        );

        // Load and cache XSL transform on background thread
        Task.Run(() => LoadXslTransform());
    }

    private void LoadXslTransform()
    {
        try
        {
            lock (_xslLock)
            {
                if (_cachedXslTransform != null) return;

                using var xslStream = FileSystem.OpenAppPackageFileAsync("tipitaka-latn.xsl").Result;
                using var xslReader = XmlReader.Create(xslStream);

                _cachedXslTransform = new XslCompiledTransform();
                _cachedXslTransform.Load(xslReader);
            }
        }
        catch
        {
            // XSL loading will be retried on first book load if needed
        }
    }

    public async Task<string> LoadBookAsHtmlAsync(string bookFileName)
    {
        try
        {
            // Load XML from shared CSTReader directory
            var xmlPath = Path.Combine(_xmlBooksPath, bookFileName);

            if (!File.Exists(xmlPath))
            {
                return $"<html><body><h1>Book file not found</h1><p>File: {xmlPath}</p><p>Directory exists: {Directory.Exists(_xmlBooksPath)}</p></body></html>";
            }

            // Read XML content - use ConfigureAwait(false) to avoid UI thread deadlocks
            string xmlContent = await File.ReadAllTextAsync(xmlPath, System.Text.Encoding.UTF8).ConfigureAwait(false);

            // Parse XML and transform on background thread to avoid blocking UI
            return await Task.Run(() =>
            {
                var xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(xmlContent);

                // Ensure XSL is loaded (should already be cached from constructor)
                lock (_xslLock)
                {
                    if (_cachedXslTransform == null)
                    {
                        LoadXslTransform();
                    }

                    if (_cachedXslTransform == null)
                    {
                        return "<html><body><h1>Could not load XSL file</h1></body></html>";
                    }

                    using var stringWriter = new StringWriter();
                    _cachedXslTransform.Transform(xmlDoc, null, stringWriter);

                    return stringWriter.ToString();
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return $"<html><body><h1>Error loading book</h1><p>{ex.Message}</p><pre>{ex.StackTrace}</pre></body></html>";
        }
    }

    public List<Book> GetAllBooks()
    {
        // Use the existing CST.Books.Inst singleton
        return CST.Books.Inst.ToList();
    }
}
