namespace TestingTask.CLI;

public class AppSettings {
    public int BatchSize { get; set; }
    public string ConnectionString { get; set; } = string.Empty;
    public string InputFilePath { get; set; } = string.Empty;
    public string DuplicatesFilePath { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
}