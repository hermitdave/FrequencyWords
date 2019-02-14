using ICSharpCode.SharpZipLib.Tar;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using LanguageDetection;
using ICSharpCode.SharpZipLib.Zip;

namespace FrequencyListBuilder
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                Console.Error.WriteLine("you need to pass directly path to start the process");
                return;
            }

            string type = args[0];

            InputType inputType = InputType.Unknown;
            if (!Enum.TryParse(type, true, out inputType))
            {
                Console.Error.WriteLine("Expected input in one of the following format");
                Console.Error.WriteLine(@"FrequencyListBuilder directory c:\MyContentDir\en");
                Console.Error.WriteLine(@"FrequencyListBuilder archive c:\MyContentDir\en.tar.gz");
                return;
            }

            string pathInput = args[1];

            //string dirPath = @"C:\OpenSubtitles2016\xml\br";
            string nameWithExtension = Path.GetFileName(pathInput);
            string languageName = null;
            string extension = null;
            if (inputType == InputType.Directory)
            {
                languageName = nameWithExtension;
            }
            else
            {
                int pos = nameWithExtension.IndexOf(".");
                languageName = nameWithExtension.Substring(0, pos);
                extension = nameWithExtension.Substring(pos);
            }

            string parentPath = Path.Combine(Path.GetDirectoryName(pathInput), languageName);

            if (!Directory.Exists(parentPath))
            {
                Directory.CreateDirectory(parentPath);
            }

            string fileLog = Path.Combine(parentPath, $"{languageName}.log");
            string fullData = Path.Combine(parentPath, $"{languageName}_full.txt");
            string partialData = Path.Combine(parentPath, $"{languageName}_50k.txt");
            string ignoredData = Path.Combine(parentPath, $"{languageName}_ignored.txt");

            Dictionary<string, long> wordFrequencyDictionary = new Dictionary<string, long>();

            var logWriter = File.CreateText(fileLog);

            try
            {
                if (inputType == InputType.Directory)
                {
                    DirectoryInfo startDir = new DirectoryInfo(pathInput);

                    ProcessFilesInDirectory(startDir, wordFrequencyDictionary, logWriter);
                }
                else
                {
                    FileInfo startFileInfo = new FileInfo(pathInput);

                    switch (extension)
                    {
                        case ".xml.gz":
                            using (var stream = startFileInfo.OpenRead())
                            {
                                try
                                {
                                    ProcessSubtitleGZ(stream, wordFrequencyDictionary, logWriter);
                                }
                                catch { }
                            }
                            break;

                        case ".zip":
                            ProcessZipArchive(startFileInfo, wordFrequencyDictionary, logWriter);
                            break;

                        case ".rar":
                            break;

                        case ".tar":
                            break;

                        case ".tar.gz":
                            ProcessTarGzArchive(startFileInfo, wordFrequencyDictionary, logWriter);
                            break;
                    }
                }

                LanguageDetector detector = null;
                try
                {
                    var languageDetector = new LanguageDetector();
                    languageDetector.AddLanguages(languageName);

                    detector = languageDetector;
                }
                catch { }
                //Assert.AreEqual("lv", detector.Detect("čau, man iet labi, un kā iet tev?"));

                List<KeyValuePair<string, long>> validWords = new List<KeyValuePair<string, long>>();
                List<KeyValuePair<string, long>> ignoredWords = new List<KeyValuePair<string, long>>();

                //var myList = wordFrequencyDictionary.ToList().FindAll(kvp => IsValidWord(kvp.Key, detector, languageName));
                wordFrequencyDictionary.ToList().ForEach((kvp) =>
                {
                    if (IsValidWord(kvp.Key, detector, languageName))
                    {
                        validWords.Add(kvp);
                    }
                    else
                    {
                        ignoredWords.Add(kvp);
                    }
                });

                validWords.Sort((pair1, pair2) => pair2.Value.CompareTo(pair1.Value));
                ignoredWords.Sort((pair1, pair2) => pair2.Value.CompareTo(pair1.Value));

                LogWordlistToFile(validWords, ignoredWords, fullData, partialData, ignoredData);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                LogMessage(logWriter, $"Error: {ex.Message}");
            }
            finally
            {
                logWriter.Flush();
                logWriter.Dispose();
            }
        }

        private static void ProcessTarGzArchive(FileInfo startFileInfo, Dictionary<string, long> wordFrequencyDictionary, StreamWriter logWriter)
        {
            HashSet<string> pathSet = new HashSet<string>();
            using (var fileStream = startFileInfo.OpenRead())
            {
                using (var decompressedStream = new GZipStream(fileStream, CompressionMode.Decompress))
                {
                    using (var tarStream = new TarInputStream(decompressedStream))
                    {
                        TarEntry tarEntry;
                        while ((tarEntry = tarStream.GetNextEntry()) != null)
                        {
                            if (tarEntry.IsDirectory)
                            {
                                continue;
                            }

                            string filePath = tarEntry.Name;
                            string dirPath = Path.GetDirectoryName(filePath);

                            if (pathSet.Contains(dirPath))
                            {
                                LogMessage(logWriter, $"Ignore file {tarEntry.Name}");

                                continue;
                            }

                            LogMessage(logWriter, $"Process directory {Path.GetDirectoryName(tarEntry.Name)}");
                            LogMessage(logWriter, $"Process file {tarEntry.Name}");
                            Console.WriteLine(tarEntry.Name);
                            pathSet.Add(dirPath);

                            using (var stream = new MemoryStream())
                            {
                                tarStream.CopyEntryContents(stream);
                                stream.Position = 0;
                                try
                                {
                                    ProcessSubtitleGZ(stream, wordFrequencyDictionary, logWriter);
                                }
                                catch { }
                            }
                        }
                    }
                }
            }
        }

        private static void ProcessZipArchive(FileInfo startFileInfo, Dictionary<string, long> wordFrequencyDictionary, StreamWriter logWriter)
        {
            HashSet<string> pathSet = new HashSet<string>();
            var archive = new ZipFile(startFileInfo.FullName);
            foreach (ZipEntry entry in archive)
            {
                if (entry.IsFile && entry.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    string directoryPath = Path.GetDirectoryName(entry.Name).ToLowerInvariant();
                    if (!pathSet.Contains(directoryPath))
                    {
                        pathSet.Add(directoryPath);

                        LogMessage(logWriter, $"Processing {entry.Name}");

                        if (!ProcessSubtitle(archive.GetInputStream(entry), wordFrequencyDictionary, logWriter))
                        {
                            LogMessage(logWriter, $"Error processing {entry.Name}");
                        }
                    }
                }
            }
        }

        private static bool IsValidWord(string word, LanguageDetector languageDetector, string languageName)
        {
            if (languageDetector != null)
            {
                var detectedLanguage = languageDetector.Detect(word);

                return detectedLanguage != null && detectedLanguage.Equals(languageName);
            }
            else
            {
                var nonCharEntries = word.Where(c => !Char.IsLetter(c));

                return !nonCharEntries.Any();
            }
        }

        private static void LogWordlistToFile(List<KeyValuePair<string, long>> validWordList, List<KeyValuePair<string, long>> ignoredWordList, string fullDataFileName, string partialDataFileName, string ignoredDataFileName)
        {
            DumpListToFile(fullDataFileName, validWordList);

            if (validWordList.Count > 50000)
            {
                DumpListToFile(partialDataFileName, validWordList.Take(50000));
            }

            DumpListToFile(ignoredDataFileName, ignoredWordList);
        }

        private static void DumpListToFile(string fullDataFileName, IEnumerable<KeyValuePair<string, long>> myList)
        {
            using (StreamWriter writer = new StreamWriter(fullDataFileName))
            {
                foreach (var entry in myList)
                {
                    writer.WriteLine($"{entry.Key} {entry.Value}");
                }

                writer.Flush();
            }
        }

        private static void ProcessFilesInDirectory(DirectoryInfo startDir, Dictionary<string, long> wordDictionary, StreamWriter logWriter)
        {
            LogMessage(logWriter, $"Process {startDir.Name} at {startDir.FullName}");

            if (!startDir.Exists)
            {
                LogMessage(logWriter, $"{startDir.FullName} does not exist. go back to parent directory");

                logWriter.WriteLine();
                return;
            }

            var subDirectories = startDir.GetDirectories();

            LogMessage(logWriter, $"{startDir.Name} has {subDirectories.Length} subdirectories");

            foreach (var subDir in subDirectories)
            {
                ProcessFilesInDirectory(subDir, wordDictionary, logWriter);
            }

            var files = startDir.GetFiles("*.xml.gz");

            LogMessage(logWriter, $"{startDir.Name} has {files.Length} files");

            ProcessFilesInDirectory(files, wordDictionary, logWriter);

            foreach (var file in files)
            {
                try
                {
                    file.Delete();
                }
                catch { }
            }

            try
            {
                startDir.Delete();
            }
            catch { }
        }

        private static void LogMessage(StreamWriter logWriter, string logMessge)
        {
            logWriter.WriteLine(logMessge);
            Console.WriteLine(logMessge);
        }

        private static void ProcessFilesInDirectory(FileInfo[] files, Dictionary<string, long> wordDictionary, StreamWriter logWriter)
        {
            if (files.Length == 0)
                return;

            var first = files.First();
            var rest = files.Skip(1);

            using (var stream = first.OpenRead())
            {
                try
                {
                    ProcessSubtitleGZ(stream, wordDictionary, logWriter);
                }
                catch { }
            }

            foreach (var extra in rest)
            {
                LogMessage(logWriter, $"Skipping {extra.FullName}");
            }
        }

        private static void ProcessSubtitleGZ(Stream stream, Dictionary<string, long> wordDictionary, StreamWriter logWriter)
        {
            using (var decompressedStream = new GZipStream(stream, CompressionMode.Decompress))
            {
                using (XmlReader fileReader = XmlReader.Create(decompressedStream))
                {
                    while (!fileReader.EOF)
                    {
                        fileReader.Read();
                        if (fileReader.Name.Equals("w"))
                        {
                            var text = fileReader.ReadInnerXml().ToLowerInvariant();
                            if (wordDictionary.ContainsKey(text))
                            {
                                wordDictionary[text]++;
                            }
                            else
                            {
                                wordDictionary[text] = 1;
                            }
                        }
                    }
                }
            }
        }

        private static bool ProcessSubtitle(Stream stream, Dictionary<string, long> wordDictionary, StreamWriter logWriter)
        {
            try
            {
                using (XmlReader fileReader = XmlReader.Create(stream))
                {
                    while (!fileReader.EOF)
                    {
                        fileReader.Read();
                        if (fileReader.Name.Equals("w"))
                        {
                            var text = fileReader.ReadInnerXml().ToLowerInvariant();
                            if (wordDictionary.ContainsKey(text))
                            {
                                wordDictionary[text]++;
                            }
                            else
                            {
                                wordDictionary[text] = 1;
                            }
                        }
                    }

                    return true;
                }
            }
            catch(Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
            }

            return false;
        }
    }
}
