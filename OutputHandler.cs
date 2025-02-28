namespace TrippinEdi;

public class OutputHandler(bool logToFile = false, string logFilePath = "background_inference.txt")
{
    public bool LogToFile
    {
        get => logToFile;
        set => logToFile = value;
    }

    public void Write(string text, ConsoleColor? color = null)
    {
        if (logToFile)
        {
            File.AppendAllText(logFilePath, text);
        }
        else if (color.HasValue)
        {
            Console.ForegroundColor = color.Value;
            Console.Write(text);
            Console.ResetColor();
        }
        else
        {
            Console.Write(text);
        }
    }

    public void WriteLine(string text, ConsoleColor? color = null)
    {
        Write(text + "\n", color);
    }
}
