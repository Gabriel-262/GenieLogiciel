using System.Globalization;
using System.Text;
using System.Xml;

namespace EasyLog;

public class XmlAppendLogger : ILogger
{
    private readonly object _fileLock = new();
    private readonly Func<string> _logPathProvider;

    public XmlAppendLogger(Func<string> logPathProvider)
    {
        _logPathProvider = logPathProvider;
    }

    public void Log(LogEntry entry)
    {
        string path = _logPathProvider();
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var sb = new StringBuilder();
        var settings = new XmlWriterSettings
        {
            Indent = true,
            OmitXmlDeclaration = true,
            ConformanceLevel = ConformanceLevel.Fragment
        };

        using (var writer = XmlWriter.Create(sb, settings))
        {
            writer.WriteStartElement("LogEntry");
            writer.WriteElementString("Timestamp", entry.Timestamp.ToString("o", CultureInfo.InvariantCulture));
            writer.WriteElementString("BackupName", entry.BackupName);
            writer.WriteElementString("Action", entry.Action.ToString());
            writer.WriteElementString("SourceFilePath", entry.SourceFilePath);
            writer.WriteElementString("DestinationFilePath", entry.DestinationFilePath);
            writer.WriteElementString("FileSizeBytes", entry.FileSizeBytes.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("TransferTimeMs", entry.TransferTimeMs.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndElement();
        }

        lock (_fileLock)
        {
            File.AppendAllText(path, sb.ToString() + Environment.NewLine + Environment.NewLine);
        }
    }
}
