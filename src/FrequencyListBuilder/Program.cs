using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

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
            string name = null;
            string extension = null;
            if(inputType == InputType.Directory)
            {
                name = nameWithExtension;
            }
            else
            {
                int pos = nameWithExtension.IndexOf(".");
                name = nameWithExtension.Substring(0, pos);
                extension = nameWithExtension.Substring(pos);
            }

            string parentPath = Path.GetDirectoryName(pathInput);
            string fileLog = Path.Combine(parentPath, $"{name}.log");
            string fullData = Path.Combine(parentPath, $"{name}_full.txt");
            string partialData = Path.Combine(parentPath, $"{name}_50k.txt");

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

                    switch(extension)
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

                var myList = wordFrequencyDictionary.ToList().FindAll(kvp => IsValidWord(kvp.Key));
                myList.Sort((pair1, pair2) => pair2.Value.CompareTo(pair1.Value));

                LogWordlistToFile(myList, fullData, partialData);
            }
            catch(Exception ex)
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

        private static bool IsValidWord(string key)
        {
            var nonCharEntries = key.Where(c => !Char.IsLetter(c));

            return !nonCharEntries.Any();
        }

        private static void LogWordlistToFile(List<KeyValuePair<string, long>> wordFrequencyList, string fullDataFileName, string partialDataFileName)
        {
            DumpListToFile(fullDataFileName, wordFrequencyList);

            DumpListToFile(partialDataFileName, wordFrequencyList.Take(50000));
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

            foreach(var extra in rest)
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

        //private static void ProcessSubtitle(Stream stream, Dictionary<string, long> wordDictionary, StreamWriter logWriter)
        //{
        //    using (XmlReader fileReader = XmlReader.Create(stream))
        //    {
        //        while (!fileReader.EOF)
        //        {
        //            fileReader.Read();
        //            if (fileReader.Name.Equals("w"))
        //            {
        //                var text = fileReader.ReadInnerXml().ToLowerInvariant();
        //                if (wordDictionary.ContainsKey(text))
        //                {
        //                    wordDictionary[text]++;
        //                }
        //                else
        //                {
        //                    wordDictionary[text] = 1;
        //                }
        //            }
        //        }
        //    }
        //}
    }
}
