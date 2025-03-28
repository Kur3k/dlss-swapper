using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSS_Swapper.Data;
using DLSS_Swapper.Helpers;
using DLSS_Swapper.Translations.Pages;
using DLSS_Swapper.UserControls;
using Microsoft.UI.Xaml.Controls;

namespace DLSS_Swapper.Pages;

public partial class LibraryPageModel : ObservableObject
{
    LibraryPage libraryPage;

    internal ObservableCollection<DLLRecord>? SelectedLibraryList { get; private set; } = null;

    public LibraryPageModel(LibraryPage libraryPage) : base()
    {
        TranslationProperties = new LibraryPageTranslationPropertiesViewModel();
        this.libraryPage = libraryPage;
    }

    [ObservableProperty]
    public partial LibraryPageTranslationPropertiesViewModel TranslationProperties { get; private set; }

    [RelayCommand]
    async Task RefreshAsync()
    {
        var didUpdate = await App.CurrentApp.MainWindow.UpdateManifestAsync();
        if (didUpdate)
        {
            App.CurrentApp.MainWindow.FilterDLLRecords();
            DLLManager.Instance.LoadLocalRecords();
        }
        else
        {
            var errorDialog = new EasyContentDialog(libraryPage.XamlRoot)
            {
                Title = ResourceHelper.GetString("Error"),
                CloseButtonText = ResourceHelper.GetString("Okay"),
                DefaultButton = ContentDialogButton.Close,
                Content = ResourceHelper.GetString("UnableToDllUpdate"),
            };
            await errorDialog.ShowAsync();
        }
    }

    [RelayCommand]
    async Task ExportAllAsync()
    {
        // Check that there are records to export first.
        var allDllRecords = new List<DLLRecord>();
        allDllRecords.AddRange(DLLManager.Instance.DLSSRecords.Where(x => x.LocalRecord?.IsDownloaded == true));
        allDllRecords.AddRange(DLLManager.Instance.DLSSGRecords.Where(x => x.LocalRecord?.IsDownloaded == true));
        allDllRecords.AddRange(DLLManager.Instance.DLSSDRecords.Where(x => x.LocalRecord?.IsDownloaded == true));
        allDllRecords.AddRange(DLLManager.Instance.FSR31DX12Records.Where(x => x.LocalRecord?.IsDownloaded == true));
        allDllRecords.AddRange(DLLManager.Instance.FSR31VKRecords.Where(x => x.LocalRecord?.IsDownloaded == true));
        allDllRecords.AddRange(DLLManager.Instance.XeSSRecords.Where(x => x.LocalRecord?.IsDownloaded == true));
        allDllRecords.AddRange(DLLManager.Instance.XeLLRecords.Where(x => x.LocalRecord?.IsDownloaded == true));
        allDllRecords.AddRange(DLLManager.Instance.XeSSFGRecords.Where(x => x.LocalRecord?.IsDownloaded == true));

        // TODO: Add local records
        //allDlssRecords.AddRange(App.CurrentApp.ImportedDLSSRecords.Where(x => x.LocalRecord?.IsDownloaded == true));

        if (allDllRecords.Count == 0)
        {
            var dialog = new EasyContentDialog(libraryPage.XamlRoot)
            {
                CloseButtonText = ResourceHelper.GetString("Okay"),
                DefaultButton = ContentDialogButton.Close,
                Title = ResourceHelper.GetString("Error"),
                Content = ResourceHelper.GetString("NoDllsToExport"),
            };
            await dialog.ShowAsync();
            return;
        }


        var exportingDialog = new EasyContentDialog(libraryPage.XamlRoot)
        {
            Title = ResourceHelper.GetString("Exporting"),
            Content = new ProgressRing()
            {
                IsIndeterminate = true,
            },
        };

        var tempExportPath = Path.Combine(Storage.GetTemp(), "export");
        var finalExportZip = string.Empty;
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentApp.MainWindow);
            var savePicker = new Windows.Storage.Pickers.FileSavePicker();
            savePicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            savePicker.FileTypeChoices.Add("Zip archive", new List<string>() { ".zip" });
            savePicker.SuggestedFileName = $"dlss_swapper_export.zip";
            WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);
            var saveFile = await savePicker.PickSaveFileAsync();

            // User cancelled.
            if (saveFile is null)
            {
                return;
            }

            finalExportZip = saveFile.Path;

            Storage.CreateDirectoryIfNotExists(tempExportPath);

            _ = exportingDialog.ShowAsync();

            // Give UI time to update and show import screen.
            await Task.Delay(50);

            var exportCount = 0;

            using (var fileStream = File.Create(finalExportZip))
            {
                using (var zipArchive = new ZipArchive(fileStream, ZipArchiveMode.Create))
                {
                    foreach (var dllRecord in allDllRecords)
                    {
                        if (dllRecord.LocalRecord is null)
                        {
                            continue;
                        }

                        // TODO: When fixing imported system, make sure to update this to use full path
                        var internalZipDir = DLLManager.Instance.GetAssetTypeName(dllRecord.AssetType);
                        if (dllRecord.LocalRecord.IsImported == true)
                        {
                            internalZipDir = Path.Combine("Imported", internalZipDir);
                        }

                        internalZipDir = Path.Combine(internalZipDir, dllRecord.DisplayName);

                        using (var dlssFileStream = File.OpenRead(dllRecord.LocalRecord.ExpectedPath))
                        {
                            using (var dlssZip = new ZipArchive(dlssFileStream, ZipArchiveMode.Read))
                            {
                                var zippedDlls = dlssZip.Entries.Where(x => x.Name.EndsWith(".dll")).ToArray();

                                // If there is more than one dll something has gone wrong.
                                if (zippedDlls.Length != 1)
                                {
                                    throw new Exception(ResourceHelper.GetFormattedResourceTemplate("ExportErrorDueToDllsCountTemplate", dllRecord.LocalRecord.ExpectedPath, zippedDlls.Length));
                                }

                                var tempFileExportPath = Path.Combine(tempExportPath, Guid.NewGuid().ToString("D"));
                                Storage.CreateDirectoryIfNotExists(tempFileExportPath);

                                var tempFile = Path.Combine(tempFileExportPath, Path.GetFileName(zippedDlls[0].Name));
                                zippedDlls[0].ExtractToFile(tempFile);
                                zipArchive.CreateEntryFromFile(tempFile, Path.Combine(internalZipDir, Path.GetFileName(tempFile)));

                                // Try clean up as we go.
                                try
                                {
                                    Directory.Delete(tempFileExportPath, true);
                                }
                                catch
                                {
                                    // NOOP
                                }
                            }
                        }

                        ++exportCount;
                    }
                }
            }

            exportingDialog.Hide();

            var dialog = new EasyContentDialog(libraryPage.XamlRoot)
            {
                CloseButtonText = ResourceHelper.GetString("Okay"),
                DefaultButton = ContentDialogButton.Close,
                Title = ResourceHelper.GetString("Success"),
                Content = ResourceHelper.GetFormattedResourceTemplate("ExportedDllsTemplate", exportCount, exportCount == 1 ? string.Empty : "s"),
            };
            await dialog.ShowAsync();
        }
        catch (Exception err)
        {
            // If we failed to export lets delete teh temp zip file that was create.
            if (string.IsNullOrEmpty(finalExportZip) == false && File.Exists(finalExportZip))
            {
                try
                {
                    if (File.Exists(finalExportZip))
                    {
                        File.Delete(finalExportZip);
                    }
                }
                catch (Exception err2)
                {
                    Logger.Error(err2);
                }
            }

            exportingDialog.Hide();

            Logger.Error(err);

            // If the fullExpectedPath does not exist, or there was an error writing it.
            var dialog = new EasyContentDialog(libraryPage.XamlRoot)
            {
                Title = ResourceHelper.GetString("Error"),
                CloseButtonText = ResourceHelper.GetString("Okay"),
                DefaultButton = ContentDialogButton.Close,
                Content = ResourceHelper.GetString("CouldntExportDlssDll"),
            };
            await dialog.ShowAsync();
        }
        finally
        {
            // Clean up temp export path.
            try
            {
                if (Directory.Exists(tempExportPath))
                {
                    Directory.Delete(tempExportPath, true);
                }
            }
            catch (Exception err)
            {
                Logger.Error(err);
            }
        }
    }


    void ImportDLL(string filename)
    {
        // TODO: Reimplement
        /*
        var versionInfo = FileVersionInfo.GetVersionInfo(filename);

        // Can't import DLSS v3 dlls at this time.
        if (versionInfo.FileDescription?.Contains("DLSS-G", StringComparison.InvariantCultureIgnoreCase) == true)
        {
            throw new Exception($"Could not import \"{filename}\", appears to be a DLSS 3 (frame generation) file.");
        }

        // Can't import if it isn't a DLSS dll file.
        if (versionInfo.FileDescription?.Contains("DLSS", StringComparison.InvariantCultureIgnoreCase) == false)
        {
            throw new Exception($"Could not import \"{filename}\", does not appear to be a DLSS dll.");
        }

        var isTrusted = WinTrust.VerifyEmbeddedSignature(filename);

        // Don't do anything with untrusted dlls.
        if (Settings.Instance.AllowUntrusted == false && isTrusted == false)
        {
            throw new Exception($"Could not import \"{filename}\", file is not trusted by Windows.");
        }

        var dllHash = versionInfo.GetMD5Hash();

        var existingImportedDlls = App.CurrentApp.ImportedDLSSRecords.Where(x => string.Equals(x.MD5Hash, dllHash, StringComparison.InvariantCultureIgnoreCase));
        // If the dll is already imported don't import it again.
        if (existingImportedDlls.Any())
        {
            throw new Exception($"Could not import \"{filename}\", file appears to have been imported previously.");
        }

        var fileInfo = new FileInfo(filename);
        var zipFilename = $"{versionInfo.GetFormattedFileVersion()}_{dllHash}.zip";
        var finalZipPath = Path.Combine(Storage.GetStorageFolder(), "imported_dlss_zip", zipFilename);
        Storage.CreateDirectoryForFileIfNotExists(finalZipPath);


        // The plan here was to check if importing is equivilant of downloading the file and if so consider it the downloaded file.
        // The zip hash (and zip filesize) does not match if we create the zip here so it seems odd to have that as a value.
        // We could potentially just consider the ziphash only used for downloading and not on disk validation.
        //var existingDLSSRecord = App.CurrentApp.DLSSRecords.GetRecordFromHash(dllHash);
        //if (existingDLSSRecord is not null)
        //{
        //    var tempExtractPath = Path.Combine(Storage.GetTemp(), "import");
        //    Storage.CreateDirectoryIfNotExists(tempExtractPath);
        //    var tempZipFile = Path.Combine(tempExtractPath, Path.GetFileNameWithoutExtension(filename)) + ".zip";
        //    using (var zipFile = File.Open(tempZipFile, FileMode.Create))
        //    {
        //        using (var zipArchive = new ZipArchive(zipFile, ZipArchiveMode.Create, true))
        //        {
        //            zipArchive.CreateEntryFromFile(filename, Path.GetFileName("nvngx_dlss.dll"));
        //        }
        //        zipFile.Position = 0;
        //        var size = zipFile.Length;
        //        // Once again, MD5 should never be used to check if a file has been tampered with.
        //        // We are simply using it to check the integrity of the downloaded/extracted file.
        //        using (var md5 = MD5.Create())
        //        {
        //            var hash = md5.ComputeHash(zipFile);
        //            var zipHash = BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
        //        }
        //    }
        //}

        var tempExtractPath = Path.Combine(Storage.GetTemp(), "import");
        Storage.CreateDirectoryIfNotExists(tempExtractPath);

        var tempZipFile = Path.Combine(tempExtractPath, zipFilename);

        var dlssRecord = new DLLRecord()
        {
            Version = versionInfo.GetFormattedFileVersion(),
            VersionNumber = versionInfo.GetFileVersionNumber(),
            MD5Hash = dllHash,
            FileSize = fileInfo.Length,
            ZipFileSize = 0,
            ZipMD5Hash = string.Empty,
            IsSignatureValid = isTrusted,
        };

        using (var zipFile = File.Open(tempZipFile, FileMode.Create))
        {
            using (var zipArchive = new ZipArchive(zipFile, ZipArchiveMode.Create, true))
            {
                zipArchive.CreateEntryFromFile(filename, Path.GetFileName("nvngx_dlss.dll"));
            }

            zipFile.Position = 0;

            dlssRecord.ZipFileSize = zipFile.Length;
            // Once again, MD5 should never be used to check if a file has been tampered with.
            // We are simply using it to check the integrity of the downloaded/extracted file.
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(zipFile);
                dlssRecord.ZipMD5Hash = BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
            }
        }

        // Move new record to where it should live in DLSS Swapper app directory.
        File.Move(tempZipFile, finalZipPath, true);

        dlssRecord.LocalRecord = LocalRecord.FromExpectedPath(finalZipPath, true);

        // Add our new record.
        App.CurrentApp.ImportedDLSSRecords.Add(dlssRecord);
        */
    }

    [RelayCommand]
    async Task ImportAsync()
    {
        if (Settings.Instance.HasShownWarning == false)
        {
            var warningDialog = new EasyContentDialog(libraryPage.XamlRoot)
            {
                Title = ResourceHelper.GetString("Warning"),
                CloseButtonText = ResourceHelper.GetString("Okay"),
                DefaultButton = ContentDialogButton.Close,
                Content = ResourceHelper.GetString("MalliciousDllsInfo"),
            };
            await warningDialog.ShowAsync();

            Settings.Instance.HasShownWarning = true;
        }

        var sorryDialog = new EasyContentDialog(libraryPage.XamlRoot)
        {
            Title = ResourceHelper.GetString("Sorry"),
            CloseButtonText = ResourceHelper.GetString("Okay"),
            DefaultButton = ContentDialogButton.Close,
            Content = ResourceHelper.GetString("ImportSystemCurrentlyBroken"),
        };
        await sorryDialog.ShowAsync();

        /*
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentApp.MainWindow);
        var openPicker = new Windows.Storage.Pickers.FileOpenPicker();
        openPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        openPicker.FileTypeFilter.Add(".dll");
        openPicker.FileTypeFilter.Add(".zip");
        WinRT.Interop.InitializeWithWindow.Initialize(openPicker, hwnd);
        var openFile = await openPicker.PickSingleFileAsync();

        // User cancelled.
        if (openFile is null)
        {
            return;
        }


        var dialog = new EasyContentDialog(libraryPage.XamlRoot)
        {
            PrimaryButtonText = "Import",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Title = "Reminder",
            Content = $"Only import DLLs from sources you trust.",
        };
        var response = await dialog.ShowAsync();
        if (response == ContentDialogResult.Primary)
        {
            if (File.Exists(openFile.Path) == false)
            {
                return;
            }

            var loadingDialog = new EasyContentDialog(libraryPage.XamlRoot)
            {
                Title = "Importing",
                // I would like this to be a progress ring but for some reason the ring will not show.
                Content = new ProgressBar()
                {
                    IsIndeterminate = true,
                },
            };
            _ = loadingDialog.ShowAsync();

            // Give UI time to update and show import screen.
            await Task.Delay(50);

            // Used only if we import a zip
            var tempExtractPath = Path.Combine(Storage.GetTemp(), "import");

            var importPartiallyFailed = false;
            try
            {
                var importSuccessCount = 0;
                var importFailureCount = 0;

                if (openFile.Path.EndsWith(".zip"))
                {
                    using (var archive = ZipFile.OpenRead(openFile.Path))
                    {
                        var zippedDlls = archive.Entries.Where(x => x.Name.EndsWith(".dll")).ToArray();
                        if (zippedDlls.Length == 0)
                        {
                            throw new Exception("Zip did not contain any dlls..");
                        }

                        Storage.CreateDirectoryIfNotExists(tempExtractPath);

                        foreach (var zippedDll in zippedDlls)
                        {
                            var tempFile = Path.Combine(tempExtractPath, $"nvngx_dlss_{Guid.NewGuid().ToString("D")}.dll");
                            zippedDll.ExtractToFile(tempFile);

                            try
                            {
                                ImportDLL(tempFile);
                                ++importSuccessCount;
                            }
                            catch (Exception)
                            {
                                importPartiallyFailed = true;
                                ++importFailureCount;
                            }

                            // Clean up temp file.
                            File.Delete(tempFile);
                        }
                    }

                    // We still save if some records have been imported.
                    if (importSuccessCount > 0)
                    {
                        await Storage.SaveImportedManifestJsonAsync();
                        App.CurrentApp.MainWindow.FilterDLLRecords();
                    }

                    if (importPartiallyFailed)
                    {
                        throw new Exception($"From the zip import, {importSuccessCount} DLSS dlls succeeded and {importFailureCount} failed.");
                    }
                }
                else
                {
                    ImportDLL(openFile.Path);
                    ++importSuccessCount;

                    await Storage.SaveImportedManifestJsonAsync();
                    App.CurrentApp.MainWindow.FilterDLLRecords();
                }

                loadingDialog.Hide();

                dialog = new EasyContentDialog(libraryPage.XamlRoot)
                {
                    CloseButtonText = "Okay",
                    DefaultButton = ContentDialogButton.Close,
                    Title = "Success",
                    Content = $"Imported {importSuccessCount} DLSS dll record{(importSuccessCount == 1 ? string.Empty : "s")}.",
                };
                await dialog.ShowAsync();
            }
            catch (Exception err)
            {
                loadingDialog.Hide();

                // Clean up tempExtractPath if it exists
                if (Directory.Exists(tempExtractPath))
                {
                    try
                    {
                        Directory.Delete(tempExtractPath, true);
                    }
                    catch (Exception err2)
                    {
                        Logger.Error(err2);
                    }
                }

                Logger.Error(err);


                // TODO: Button to open error log
                dialog = new EasyContentDialog(libraryPage.XamlRoot)
                {
                    CloseButtonText = "Okay",
                    DefaultButton = ContentDialogButton.Close,
                    Title = "Error",
                    Content = $"Could not import record. Please see your error log for more information.",
                };
                await dialog.ShowAsync();
            }
        }
        */
    }

    [RelayCommand]
    async Task DeleteRecordAsync(DLLRecord record)
    {
        if (record.LocalRecord is null)
        {
            Logger.Error("Could not delete record, LocalRecord is null.");
            return;
        }

        var assetTypeName = DLLManager.Instance.GetAssetTypeName(record.AssetType);
        var dialog = new EasyContentDialog(libraryPage.XamlRoot)
        {
            PrimaryButtonText = ResourceHelper.GetString("Delete"),
            CloseButtonText = ResourceHelper.GetString("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            Content = ResourceHelper.GetFormattedResourceTemplate("DeleteDllVersionTemplate", assetTypeName, record.Version),
        };
        var response = await dialog.ShowAsync();
        if (response == ContentDialogResult.Primary)
        {
            var didDelete = record.LocalRecord.Delete();
            if (didDelete)
            {
                if (record.LocalRecord.IsImported)
                {
                    // TODO: What to do here?
                    //DLLManager.Instance.DeleteImportedDllRecord(record)
                    //App.CurrentApp.ImportedDLSSRecords.Remove(record);
                    await Storage.SaveImportedManifestJsonAsync();
                    App.CurrentApp.MainWindow.FilterDLLRecords();
                }
                else
                {
                    record.NotifyPropertyChanged(nameof(record.LocalRecord));
                }
            }
            else
            {
                var errorDialog = new EasyContentDialog(libraryPage.XamlRoot)
                {
                    Title = ResourceHelper.GetString("Error"),
                    CloseButtonText = ResourceHelper.GetString("Okay"),
                    DefaultButton = ContentDialogButton.Close,
                    Content = ResourceHelper.GetFormattedResourceTemplate("UnableToDeleteRecord", assetTypeName),
                };
                await errorDialog.ShowAsync();
            }
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    async Task DownloadRecordAsync(DLLRecord record)
    {
        var result = await record.DownloadAsync();
        if (result.Success is false && result.Cancelled is false)
        {
            var dialog = new EasyContentDialog(libraryPage.XamlRoot)
            {
                Title = ResourceHelper.GetString("Error"),
                CloseButtonText = ResourceHelper.GetString("Okay"),
                DefaultButton = ContentDialogButton.Close,
                Content = result.Message,
            };

            await dialog.ShowAsync();
        }
    }

    [RelayCommand]
    async Task CancelDownloadRecordAsync(DLLRecord record)
    {
        record?.CancelDownload();
        await Task.Delay(10);
    }

    [RelayCommand]
    async Task ExportRecordAsync(DLLRecord record)
    {
        if (record.LocalRecord is null)
        {
            return;
        }

        var exportingDialog = new EasyContentDialog(libraryPage.XamlRoot)
        {
            Title = ResourceHelper.GetString("Exporting"),
            // I would like this to be a progress ring but for some reason the ring will not show.
            Content = new ProgressRing()
            {
                IsIndeterminate = true,
            },
        };

        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentApp.MainWindow);
            var savePicker = new Windows.Storage.Pickers.FileSavePicker();
            savePicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            savePicker.FileTypeChoices.Add("Zip archive", new List<string>() { ".zip" });
            savePicker.SuggestedFileName = $"dlss_swapper_export_{record.DisplayName.Replace(" ", "_")}.zip";
            WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);
            var saveFile = await savePicker.PickSaveFileAsync();

            if (saveFile is not null)
            {
                // This will likley not be seen, but keeping it here incase export is very slow (eg. copy over very slow network).
                _ = exportingDialog.ShowAsync();

                // Give UI time to update and show import screen.
                await Task.Delay(50);

                File.Copy(record.LocalRecord.ExpectedPath, saveFile.Path, true);

                exportingDialog.Hide();

                var dialog = new EasyContentDialog(libraryPage.XamlRoot)
                {
                    Title = ResourceHelper.GetString("Success"),
                    CloseButtonText = ResourceHelper.GetString("Okay"),
                    DefaultButton = ContentDialogButton.Close,
                    Content = ResourceHelper.GetFormattedResourceTemplate("ExportedDllTemplate", record.DisplayName),
                };
                await dialog.ShowAsync();
            }
        }
        catch (Exception err)
        {
            exportingDialog.Hide();
            Logger.Error(err);

            // If the fullExpectedPath does not exist, or there was an error writing it.
            var dialog = new EasyContentDialog(libraryPage.XamlRoot)
            {
                Title = ResourceHelper.GetString("Error"),
                CloseButtonText = ResourceHelper.GetString("Okay"),
                DefaultButton = ContentDialogButton.Close,
                Content = ResourceHelper.GetString("CouldntExportDll"),
            };
            await dialog.ShowAsync();
        }
    }

    [RelayCommand]
    async Task ShowDownloadErrorAsync(DLLRecord record)
    {
        var dialog = new EasyContentDialog(libraryPage.XamlRoot)
        {
            Title = ResourceHelper.GetString("Error"),
            CloseButtonText = ResourceHelper.GetString("Okay"),
            Content = record.LocalRecord?.DownloadErrorMessage ?? ResourceHelper.GetString("CouldntDownloadNow"),
        };
        await dialog.ShowAsync();
    }

    internal void SelectLibrary(GameAssetType gameAssetType)
    {

        var newList = gameAssetType switch
        {
            GameAssetType.DLSS => DLLManager.Instance.DLSSRecords,
            GameAssetType.DLSS_G => DLLManager.Instance.DLSSGRecords,
            GameAssetType.DLSS_D => DLLManager.Instance.DLSSDRecords,
            GameAssetType.FSR_31_DX12 => DLLManager.Instance.FSR31DX12Records,
            GameAssetType.FSR_31_VK => DLLManager.Instance.FSR31VKRecords,
            GameAssetType.XeSS => DLLManager.Instance.XeSSRecords,
            GameAssetType.XeLL => DLLManager.Instance.XeLLRecords,
            GameAssetType.XeSS_FG => DLLManager.Instance.XeSSFGRecords,
            _ => null,
        };
        SelectedLibraryList = null;
        SelectedLibraryList = newList;
        OnPropertyChanged(nameof(SelectedLibraryList));
    }
}
