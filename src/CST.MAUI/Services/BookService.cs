using System.Xml;
using System.Xml.Xsl;
using CST;
using CST.Conversion;

namespace CST.MAUI.Services;

public class BookService
{
    private readonly string _xmlBooksPath;
    private readonly Dictionary<Script, XslCompiledTransform> _cachedXslTransforms = new();
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

        // Load and cache XSL transforms on background thread
        Task.Run(() => LoadXslTransforms());
    }

    private void LoadXslTransforms()
    {
        // Load the most common scripts first (Latin, Devanagari, Thai, Myanmar)
        var priorityScripts = new[] { Script.Latin, Script.Devanagari, Script.Thai, Script.Myanmar };

        foreach (var script in priorityScripts)
        {
            try
            {
                LoadXslTransform(script);
            }
            catch
            {
                // Continue loading other scripts even if one fails
            }
        }
    }

    private void LoadXslTransform(Script script)
    {
        lock (_xslLock)
        {
            if (_cachedXslTransforms.ContainsKey(script)) return;

            var xslFileName = GetXslFileName(script);
            using var xslStream = FileSystem.OpenAppPackageFileAsync(xslFileName).Result;
            using var xslReader = XmlReader.Create(xslStream);

            var transform = new XslCompiledTransform();
            transform.Load(xslReader);

            _cachedXslTransforms[script] = transform;
        }
    }

    private string GetXslFileName(Script script)
    {
        return script switch
        {
            Script.Bengali => "tipitaka-beng.xsl",
            Script.Cyrillic => "tipitaka-cyrl.xsl",
            Script.Devanagari => "tipitaka-deva.xsl",
            Script.Gujarati => "tipitaka-gujr.xsl",
            Script.Gurmukhi => "tipitaka-guru.xsl",
            Script.Kannada => "tipitaka-knda.xsl",
            Script.Khmer => "tipitaka-khmr.xsl",
            Script.Latin => "tipitaka-latn.xsl",
            Script.Malayalam => "tipitaka-mlym.xsl",
            Script.Myanmar => "tipitaka-mymr.xsl",
            Script.Sinhala => "tipitaka-sinh.xsl",
            Script.Telugu => "tipitaka-telu.xsl",
            Script.Thai => "tipitaka-thai.xsl",
            Script.Tibetan => "tipitaka-tibt.xsl",
            _ => "tipitaka-latn.xsl"
        };
    }

    public async Task<string> LoadBookAsHtmlAsync(string bookFileName, Script script = Script.Latin)
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

                // Apply script conversion if needed - XML is stored in Devanagari
                // Port of CST.Avalonia BookDisplayViewModel.cs:700-706
                if (script != Script.Devanagari)
                {
                    var convertedXml = ScriptConverter.ConvertBook(xmlDoc.OuterXml, script);
                    xmlDoc.LoadXml(convertedXml);
                }

                // Ensure XSL is loaded (load on-demand if not already cached)
                lock (_xslLock)
                {
                    if (!_cachedXslTransforms.ContainsKey(script))
                    {
                        LoadXslTransform(script);
                    }

                    if (!_cachedXslTransforms.TryGetValue(script, out var transform))
                    {
                        return $"<html><body><h1>Could not load XSL file for script: {script}</h1></body></html>";
                    }

                    using var stringWriter = new StringWriter();
                    transform.Transform(xmlDoc, null, stringWriter);

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
