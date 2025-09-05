using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Serialization;
using System.Text.Json;

namespace MetadataProcessor
{
    public static class MediaProcessor
    {
        // Unterstützte Dateiendungen
        private static readonly string[] ImageExtensions = { ".arw", ".gif", ".png", ".jpg", ".jpeg", ".raw" };
        private static readonly string[] VideoExtensions = { ".mp4", ".mov", ".avi", ".mkv", ".mp" };

        /// <summary>
        /// Durchsucht den Root-Ordner rekursiv nach JSON-Dateien, parst sie und verarbeitet die darin angegebenen Mediendateien.
        /// </summary>
        public static void ProcessRootFolder(string rootFolder)
        {
            var jsonFiles = Directory.EnumerateFiles(rootFolder, "*.json", SearchOption.AllDirectories);
            foreach (string jsonFile in jsonFiles)
            {
                ProcessJsonFile(jsonFile);
            }
        }

        private static void ProcessJsonFile(string jsonFile)
        {
            string mediaFileName = ExtractMediaFilePathFromJson(jsonFile);
            if (mediaFileName is null)
                return;
            string mediaFilePath = Path.Combine(Path.GetDirectoryName(jsonFile), mediaFileName);

            // Bei Original-Dateien gibt es randomn suffixes, z.B. "original_xyz_I.jpg", oder "original_xyz_L.jpg"
            if (!File.Exists(mediaFilePath))
            {
                // Originaldatei hat gleichen Namen wie json file nur mit suffix - können auch mehrere Dateien sein
                string mediaFileBase = Path.GetFileNameWithoutExtension(jsonFile);
                string mediaFileExt = Path.GetExtension(mediaFileName);
                var dir = Path.GetDirectoryName(jsonFile);
                var matchingFiles = Directory.EnumerateFiles(dir, mediaFileBase + "*" + mediaFileExt);
                if (matchingFiles.Any())
                {
                    foreach (var file in matchingFiles)
                    {
                        ProcessMediaFile(jsonFile, file);
                    }
                    mediaFilePath = GetMPFilePath(jsonFile);
                    if (File.Exists(mediaFilePath))
                        ProcessMediaFile(jsonFile, mediaFilePath);
                    // JSON-Datei löschen, nachdem die Metadaten erfolgreich aktualisiert wurden  
                    File.Delete(jsonFile);
                    return;
                }
            }

            if (string.IsNullOrEmpty(mediaFilePath) || !File.Exists(mediaFilePath))
            {
                Console.WriteLine("No valid media file found in JSON-File: " + jsonFile);
                return;
            } else
            {
                ProcessMediaFile(jsonFile, mediaFilePath);
                // JSON-Datei löschen, nachdem die Metadaten erfolgreich aktualisiert wurden  
                File.Delete(jsonFile);
            }
        }

        private static void ProcessMediaFile(string jsonFile, string mediaFilePath)
        {
            double? latitude, longitude, altitude;
            DateTime? dateTaken;
            GetMetadataFromJson(jsonFile, out dateTaken, out latitude, out longitude, out altitude);
            if (dateTaken.HasValue)
            {
                if (IsImageFile(mediaFilePath))
                {
                    Console.WriteLine($"Processing Image file {mediaFilePath}");
                    if (SetImageMetadata(mediaFilePath, dateTaken.Value, latitude, longitude, altitude))
                    {
                        ProcessAdditionalFiles(jsonFile, mediaFilePath);
                    }
                }
                else if (IsVideoFile(mediaFilePath))
                {
                    Console.WriteLine($"Processing Video file {mediaFilePath}");
                    if (SetVideoMetadata(mediaFilePath, dateTaken.Value))
                        File.Delete(jsonFile);
                }
            }
            else
            {
                Console.WriteLine("No date found in JSON-File for " + mediaFilePath);
            }
        }

        private static void ProcessAdditionalFiles(string jsonFile, string mediaFilePath)
        {
            double? latitude, longitude, altitude;
            DateTime? dateTaken;
            GetMetadataFromJson(jsonFile, out dateTaken, out latitude, out longitude, out altitude);
            // Wenn die .MP-Datei existiert, auch deren Metadaten aktualisieren  
            string mpFilePath = GetMPFilePath(mediaFilePath);
            if (!string.IsNullOrEmpty(mpFilePath) && File.Exists(mpFilePath))
            {
                if (SetMPMetadata(mpFilePath, dateTaken.Value))
                {
                    Console.WriteLine("Metadata updated for MP file: " + mpFilePath);
                }
            }

            // Wenn "-edited" existiert, auch deren Metdadaten aktualisieren
            string editedFilePath = Path.Combine(
                Path.GetDirectoryName(mediaFilePath),
                Path.GetFileNameWithoutExtension(mediaFilePath) + "-edited" + Path.GetExtension(mediaFilePath)
);
            if (File.Exists(editedFilePath))
            {
                if (SetImageMetadata(editedFilePath, dateTaken.Value, latitude, longitude, altitude))
                {
                    Console.WriteLine("Metadata updated for edited file: " + editedFilePath);
                }
            }

            // Wenn "-bearbeitet" existiert, auch deren Metdadaten aktualisieren
            string bearbeitetFilePath = Path.Combine(
                Path.GetDirectoryName(mediaFilePath),
                Path.GetFileNameWithoutExtension(mediaFilePath) + "-bearbeitet" + Path.GetExtension(mediaFilePath)
                );
            if (File.Exists(bearbeitetFilePath))
            {
                if (SetImageMetadata(bearbeitetFilePath, dateTaken.Value, latitude, longitude, altitude))
                {
                    Console.WriteLine("Metadata updated for bearbeitet file: " + bearbeitetFilePath);
                }
            }
        }

        private static string TryGetOriginalFileName(string jsonFile, string mediaFileName, string mediaFilePath)
        {
            string mediaFileBase = Path.GetFileNameWithoutExtension(jsonFile);
            string mediaFileExt = Path.GetExtension(mediaFileName);
            var dir = Path.GetDirectoryName(jsonFile);
            var matchingFiles = Directory.EnumerateFiles(dir, mediaFileBase + "*" + mediaFileExt);
            if (matchingFiles.Any())
            {
                mediaFilePath = matchingFiles.First();
            }

            return mediaFilePath;
        }

        private static bool IsImageFile(string file)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            return ImageExtensions.Contains(ext);
        }

        private static bool IsVideoFile(string file)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            return VideoExtensions.Contains(ext);
        }

        private static string ExtractMediaFilePathFromJson(string jsonFile)
        {
            try
            {
                string jsonContent = File.ReadAllText(jsonFile);
                using (JsonDocument doc = JsonDocument.Parse(jsonContent))
                {
                    JsonElement root = doc.RootElement;
                    if (root.TryGetProperty("title", out JsonElement titleElement))
                    {
                        return titleElement.GetString();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error reading JSON-File " + jsonFile + ": " + ex.Message);
            }
            return null;
        }

        private static DateTime? ExtractDateFromJson(string jsonFile)
        {
            try
            {
                string jsonContent = File.ReadAllText(jsonFile);
                using (JsonDocument doc = JsonDocument.Parse(jsonContent))
                {
                    JsonElement root = doc.RootElement;
                    long timestamp;
                    if (TryExtractTimestamp(root, "photoTakenTime", out timestamp) ||
                        TryExtractTimestamp(root, "creationTime", out timestamp))
                    {
                        return UnixTimeStampToDateTime(timestamp);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error reading JSON-File " + jsonFile + ": " + ex.Message);
            }
            return null;
        }

        private static bool TryExtractTimestamp(JsonElement root, string key, out long timestamp)
        {
            timestamp = 0;
            if (root.TryGetProperty(key, out JsonElement element))
            {
                if (element.TryGetProperty("timestamp", out JsonElement tsElement))
                {
                    if (tsElement.ValueKind == JsonValueKind.String)
                    {
                        if (long.TryParse(tsElement.GetString(), out timestamp))
                            return true;
                    }
                    else if (tsElement.ValueKind == JsonValueKind.Number)
                    {
                        if (tsElement.TryGetInt64(out timestamp))
                            return true;
                    }
                }
            }
            return false;
        }

        private static DateTime UnixTimeStampToDateTime(long unixTimeStamp)
        {
            DateTime dt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            dt = dt.AddSeconds(unixTimeStamp).ToLocalTime();
            return dt;
        }

        private static bool SetImageMetadata(string imagePath, DateTime dateTaken, double? latitude, double? longitude, double? altitude)
        {
            bool success = true;
            try
            {
                using (Image image = Image.FromFile(imagePath))
                {
                    // Verwende ein vorhandenes PropertyItem als Vorlage  
                    PropertyItem propItem = image.PropertyItems.Length > 0 ? image.PropertyItems[0] : null;
                    if (propItem == null)
                    {
                        // Wenn keine PropertyItems vorhanden sind, erstellen wir neue PropertyItems für die Zeitstempel  
                        propItem = (PropertyItem)FormatterServices.GetUninitializedObject(typeof(PropertyItem));
                    }

                    // Format: "YYYY:MM:DD HH:MM:SS" (abgeschlossen mit Null-Byte)  
                    string dateStr = dateTaken.ToString("yyyy:MM:dd HH:mm:ss\0");
                    byte[] dateBytes = System.Text.Encoding.ASCII.GetBytes(dateStr);
                    // EXIF-Tags: DateTime, DateTimeOriginal, DateTimeDigitized  
                    int[] tags = { 0x0132, 0x9003, 0x9004 };

                    foreach (int tag in tags)
                    {
                        propItem.Id = tag;
                        propItem.Type = 2;  // ASCII  
                        propItem.Len = dateBytes.Length;
                        propItem.Value = dateBytes;
                        image.SetPropertyItem(propItem);
                    }

                    // Geo-Daten hinzufügen  
                    if (latitude.HasValue && longitude.HasValue)
                    {
                        AddGeoDataToImage(image, latitude.Value, longitude.Value, altitude);
                    }

                    // Temporäre Datei erstellen, da das Bild gerade verwendet wird  
                    string tempFile = Path.GetTempFileName();
                    image.Save(tempFile);

                    // Bild-Objekt freigeben, um die Datei nicht zu sperren  
                    image.Dispose();

                    // Originaldatei überschreiben  
                    File.Copy(tempFile, imagePath, true);
                    File.Delete(tempFile);

                    // Setze zusätzlich die Dateisystem-Daten  
                    File.SetCreationTime(imagePath, dateTaken);
                    File.SetLastWriteTime(imagePath, dateTaken);
                }
            }
            catch (Exception ex)
            {
                success = false;
                Console.WriteLine("Error updating metadata for " + imagePath + ": " + ex.Message);
            }
            return success;
        }

        private static void AddGeoDataToImage(Image image, double latitude, double longitude, double? altitude)
        {
            // Latitude  
            PropertyItem latItem = (PropertyItem)FormatterServices.GetUninitializedObject(typeof(PropertyItem));
            latItem.Id = 0x0002;  // GPSLatitude  
            latItem.Type = 5;  // Rational  
            latItem.Len = 24;
            latItem.Value = ConvertToRational(latitude);
            image.SetPropertyItem(latItem);

            // Latitude Ref  
            PropertyItem latRefItem = (PropertyItem)FormatterServices.GetUninitializedObject(typeof(PropertyItem));
            latRefItem.Id = 0x0001;  // GPSLatitudeRef  
            latRefItem.Type = 2;  // ASCII  
            latRefItem.Len = 2;
            latRefItem.Value = new byte[] { (byte)(latitude >= 0 ? 'N' : 'S'), 0 };
            image.SetPropertyItem(latRefItem);

            // Longitude  
            PropertyItem lonItem = (PropertyItem)FormatterServices.GetUninitializedObject(typeof(PropertyItem));
            lonItem.Id = 0x0004;  // GPSLongitude  
            lonItem.Type = 5;  // Rational  
            lonItem.Len = 24;
            lonItem.Value = ConvertToRational(longitude);
            image.SetPropertyItem(lonItem);

            // Longitude Ref  
            PropertyItem lonRefItem = (PropertyItem)FormatterServices.GetUninitializedObject(typeof(PropertyItem));
            lonRefItem.Id = 0x0003;  // GPSLongitudeRef  
            lonRefItem.Type = 2;  // ASCII  
            lonRefItem.Len = 2;
            lonRefItem.Value = new byte[] { (byte)(longitude >= 0 ? 'E' : 'W'), 0 };
            image.SetPropertyItem(lonRefItem);

            // Altitude  
            if (altitude.HasValue)
            {
                PropertyItem altItem = (PropertyItem)FormatterServices.GetUninitializedObject(typeof(PropertyItem));
                altItem.Id = 0x0006;  // GPSAltitude  
                altItem.Type = 5;  // Rational  
                altItem.Len = 8;
                altItem.Value = ConvertToRational(altitude.Value);
                image.SetPropertyItem(altItem);
            }
        }

        private static byte[] ConvertToRational(double value)
        {
            int degrees = (int)value;
            value = (value - degrees) * 60;
            int minutes = (int)value;
            int seconds = (int)((value - minutes) * 60 * 100);

            byte[] result = new byte[24];
            BitConverter.GetBytes(degrees).CopyTo(result, 0);
            BitConverter.GetBytes(1).CopyTo(result, 4);
            BitConverter.GetBytes(minutes).CopyTo(result, 8);
            BitConverter.GetBytes(1).CopyTo(result, 12);
            BitConverter.GetBytes(seconds).CopyTo(result, 16);
            BitConverter.GetBytes(100).CopyTo(result, 20);

            return result;
        }

        private static void GetMetadataFromJson(string jsonFile, out DateTime? dateTaken, out double? latitude, out double? longitude, out double? altitude)
        {
            dateTaken = null;
            latitude = null;
            longitude = null;
            altitude = null;
            try
            {
                string jsonContent = File.ReadAllText(jsonFile);
                using (JsonDocument doc = JsonDocument.Parse(jsonContent))
                {
                    JsonElement root = doc.RootElement;
                    long timestamp;
                    if (TryExtractTimestamp(root, "photoTakenTime", out timestamp) ||
                        TryExtractTimestamp(root, "creationTime", out timestamp))
                    {
                        if (root.TryGetProperty("geoData", out JsonElement geoData))
                        {
                            if (geoData.TryGetProperty("latitude", out JsonElement latElement))
                            {
                                latitude = latElement.GetDouble();
                            }
                            if (geoData.TryGetProperty("longitude", out JsonElement lonElement))
                            {
                                longitude = lonElement.GetDouble();
                            }
                            if (geoData.TryGetProperty("altitude", out JsonElement altElement))
                            {
                                altitude = altElement.GetDouble();
                            }
                        }
                        dateTaken = UnixTimeStampToDateTime(timestamp);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error reading JSON-File " + jsonFile + ": " + ex.Message);
            }
        }

        private static string GetMPFilePath(string imagePath)
        {
            string mediaFileName = Path.GetFileNameWithoutExtension(imagePath);
            string mediaDir = Path.GetDirectoryName(imagePath);
            string mpFilePath = Path.Combine(mediaDir, mediaFileName);

            // MP-Datei ohne Extension
            if (File.Exists(mpFilePath))
                return mpFilePath;
            string searchFilePath;
            // MP4-Datei
            searchFilePath = Path.ChangeExtension(mpFilePath, ".MP4");
            if (File.Exists(searchFilePath))
                return searchFilePath;
            // MP-Datei mit Extension
            searchFilePath = Path.ChangeExtension(mpFilePath, ".MP");
            if (File.Exists(searchFilePath))
                return searchFilePath;
            // MP-Datei mit Extension und PX-Prefix
            searchFilePath = Path.Combine(mediaDir, mediaFileName + "_PX.MP");
            if (File.Exists(searchFilePath))
                return searchFilePath;
            searchFilePath = Path.Combine(mediaDir, mediaFileName + "PX.MP");
            if (File.Exists(searchFilePath))
                return searchFilePath;

            return null;
        }

        private static bool SetMPMetadata(string mpFilePath, DateTime dateTaken)
        {
            bool success = true;
            try
            {
                // Da keine EXIF-Daten vorhanden sind, gehen wir davon aus, dass die .MP-Datei einfach die Dateiattribute benötigt.
                // Setze die Dateisystem-Metadaten
                File.SetCreationTime(mpFilePath, dateTaken);
                File.SetLastWriteTime(mpFilePath, dateTaken);
                //Console.WriteLine("File-Attributes updated for MP file: " + mpFilePath);
            }
            catch (Exception ex)
            {
                success = false;
                Console.WriteLine("Error updating metadata for MP file " + mpFilePath + ": " + ex.Message);
            }
            return success;
        }

        private static bool SetVideoMetadata(string videoPath, DateTime dateTaken)
        {
            bool success = true;
            try
            {
                File.SetCreationTime(videoPath, dateTaken);
                File.SetLastWriteTime(videoPath, dateTaken);
                //Console.WriteLine("File-Attributes updated for " + videoPath);
            }
            catch (Exception ex)
            {
                success = false;
                Console.WriteLine("Error updating metadata for " + videoPath + ": " + ex.Message);
            }
            return success;
        }
    }
}