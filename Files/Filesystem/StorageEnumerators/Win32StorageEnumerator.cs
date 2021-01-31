﻿using ByteSizeLib;
using Files.Extensions;
using Files.Helpers;
using Microsoft.Toolkit.Uwp.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.AppService;
using Windows.Foundation.Collections;
using Windows.Storage;
using static Files.Helpers.NativeFindStorageItemHelper;
using FileAttributes = System.IO.FileAttributes;

namespace Files.Filesystem.StorageEnumerators
{
    public static class Win32StorageEnumerator
    {
        public static async Task<List<ListedItem>> ListEntries(
            string path,
            string returnformat,
            IntPtr hFile,
            WIN32_FIND_DATA findData,
            AppServiceConnection connection,
            CancellationToken cancellationToken,
            List<string> skipItems,
            Func<List<ListedItem>, Task> intermediateAction
        )
        {
            var sampler = new IntervalSampler(500);
            var tempList = new List<ListedItem>();
            var hasNextFile = false;
            var count = 0;

            do
            {
                if (((FileAttributes)findData.dwFileAttributes & FileAttributes.System) != FileAttributes.System || !App.AppSettings.AreSystemItemsHidden)
                {
                    if (((FileAttributes)findData.dwFileAttributes & FileAttributes.Hidden) != FileAttributes.Hidden || App.AppSettings.AreHiddenItemsVisible)
                    {
                        if (((FileAttributes)findData.dwFileAttributes & FileAttributes.Directory) != FileAttributes.Directory)
                        {
                            var file = await GetFile(findData, path, returnformat, connection, cancellationToken);
                            if (file != null)
                            {
                                if (skipItems?.Contains(file.ItemPath) ?? false)
                                {
                                    skipItems.Remove(file.ItemPath);
                                }
                                else
                                {
                                    tempList.Add(file);
                                }
                                ++count;
                            }
                        }
                        else if (((FileAttributes)findData.dwFileAttributes & FileAttributes.Directory) == FileAttributes.Directory)
                        {
                            if (findData.cFileName != "." && findData.cFileName != "..")
                            {
                                var folder = GetFolder(findData, path, returnformat, cancellationToken);
                                if (folder != null)
                                {
                                    if (skipItems?.Contains(folder.ItemPath) ?? false)
                                    {
                                        skipItems.Remove(folder.ItemPath);
                                    }
                                    else
                                    {
                                        tempList.Add(folder);
                                    }
                                    ++count;
                                }
                            }
                        }
                    }
                }
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                hasNextFile = FindNextFile(hFile, out findData);
                if (intermediateAction != null && (count == 32 || sampler.CheckNow()))
                {
                    await intermediateAction(tempList);
                    // clear the temporary list every time we do an intermediate action
                    tempList.Clear();
                }
            } while (hasNextFile);

            FindClose(hFile);
            return tempList;
        }

        public static ListedItem GetFolder(
            WIN32_FIND_DATA findData,
            string pathRoot,
            string dateReturnFormat,
            CancellationToken cancellationToken
        )
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            DateTime itemDate;
            try
            {
                FileTimeToSystemTime(ref findData.ftLastWriteTime, out SYSTEMTIME systemTimeOutput);
                itemDate = new DateTime(
                    systemTimeOutput.Year, systemTimeOutput.Month, systemTimeOutput.Day,
                    systemTimeOutput.Hour, systemTimeOutput.Minute, systemTimeOutput.Second, systemTimeOutput.Milliseconds,
                    DateTimeKind.Utc);
            }
            catch (ArgumentException)
            {
                // Invalid date means invalid findData, do not add to list
                return null;
            }
            var itemPath = Path.Combine(pathRoot, findData.cFileName);

            bool isHidden = (((FileAttributes)findData.dwFileAttributes & FileAttributes.Hidden) == FileAttributes.Hidden);
            double opacity = 1;

            if (isHidden)
            {
                opacity = 0.4;
            }

            return new ListedItem(null, dateReturnFormat)
            {
                PrimaryItemAttribute = StorageItemTypes.Folder,
                ItemName = findData.cFileName,
                ItemDateModifiedReal = itemDate,
                ItemType = "FileFolderListItem".GetLocalized(),
                LoadFolderGlyph = true,
                FileImage = null,
                IsHiddenItem = isHidden,
                Opacity = opacity,
                LoadFileIcon = false,
                ItemPath = itemPath,
                LoadUnknownTypeGlyph = false,
                FileSize = null,
                FileSizeBytes = 0,
                ContainsFilesOrFolders = FolderHelpers.CheckForFilesFolders(itemPath),
                //FolderTooltipText = tooltipString,
            };
        }

        public static async Task<ListedItem> GetFile(
            WIN32_FIND_DATA findData,
            string pathRoot,
            string dateReturnFormat,
            AppServiceConnection connection,
            CancellationToken cancellationToken
        )
        {
            var itemPath = Path.Combine(pathRoot, findData.cFileName);

            string itemName;
            if (App.AppSettings.ShowFileExtensions && !findData.cFileName.EndsWith(".lnk") && !findData.cFileName.EndsWith(".url"))
            {
                itemName = findData.cFileName; // never show extension for shortcuts
            }
            else
            {
                if (findData.cFileName.StartsWith("."))
                {
                    itemName = findData.cFileName; // Always show full name for dotfiles.
                }
                else
                {
                    itemName = Path.GetFileNameWithoutExtension(itemPath);
                }
            }

            DateTime itemModifiedDate, itemCreatedDate, itemLastAccessDate;
            try
            {
                FileTimeToSystemTime(ref findData.ftLastWriteTime, out SYSTEMTIME systemModifiedDateOutput);
                itemModifiedDate = new DateTime(
                    systemModifiedDateOutput.Year, systemModifiedDateOutput.Month, systemModifiedDateOutput.Day,
                    systemModifiedDateOutput.Hour, systemModifiedDateOutput.Minute, systemModifiedDateOutput.Second, systemModifiedDateOutput.Milliseconds,
                    DateTimeKind.Utc);

                FileTimeToSystemTime(ref findData.ftCreationTime, out SYSTEMTIME systemCreatedDateOutput);
                itemCreatedDate = new DateTime(
                    systemCreatedDateOutput.Year, systemCreatedDateOutput.Month, systemCreatedDateOutput.Day,
                    systemCreatedDateOutput.Hour, systemCreatedDateOutput.Minute, systemCreatedDateOutput.Second, systemCreatedDateOutput.Milliseconds,
                    DateTimeKind.Utc);

                FileTimeToSystemTime(ref findData.ftLastAccessTime, out SYSTEMTIME systemLastAccessOutput);
                itemLastAccessDate = new DateTime(
                    systemLastAccessOutput.Year, systemLastAccessOutput.Month, systemLastAccessOutput.Day,
                    systemLastAccessOutput.Hour, systemLastAccessOutput.Minute, systemLastAccessOutput.Second, systemLastAccessOutput.Milliseconds,
                    DateTimeKind.Utc);
            }
            catch (ArgumentException)
            {
                // Invalid date means invalid findData, do not add to list
                return null;
            }

            long itemSizeBytes = findData.GetSize();
            var itemSize = ByteSize.FromBytes(itemSizeBytes).ToBinaryString().ConvertSizeAbbreviation();
            string itemType = "ItemTypeFile".GetLocalized();
            string itemFileExtension = null;

            if (findData.cFileName.Contains('.'))
            {
                itemFileExtension = Path.GetExtension(itemPath);
                itemType = itemFileExtension.Trim('.') + " " + itemType;
            }

            bool itemFolderImgVis = false;
            bool itemThumbnailImgVis;
            bool itemEmptyImgVis;

            itemEmptyImgVis = true;
            itemThumbnailImgVis = false;

            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            if (findData.cFileName.EndsWith(".lnk") || findData.cFileName.EndsWith(".url"))
            {
                if (connection != null)
                {
                    var response = await connection.SendMessageAsync(new ValueSet()
                    {
                        { "Arguments", "FileOperation" },
                        { "fileop", "ParseLink" },
                        { "filepath", itemPath }
                    });
                    // If the request was canceled return now
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return null;
                    }
                    if (response.Status == AppServiceResponseStatus.Success
                        && response.Message.ContainsKey("TargetPath"))
                    {
                        var isUrl = findData.cFileName.EndsWith(".url");
                        string target = (string)response.Message["TargetPath"];
                        bool containsFilesOrFolders = false;

                        if ((bool)response.Message["IsFolder"])
                        {
                            containsFilesOrFolders = FolderHelpers.CheckForFilesFolders(target);
                        }

                        bool isHidden = (((FileAttributes)findData.dwFileAttributes & FileAttributes.Hidden) == FileAttributes.Hidden);
                        double opacity = 1;

                        if (isHidden)
                        {
                            opacity = 0.4;
                        }

                        return new ShortcutItem(null, dateReturnFormat)
                        {
                            PrimaryItemAttribute = (bool)response.Message["IsFolder"] ? StorageItemTypes.Folder : StorageItemTypes.File,
                            FileExtension = itemFileExtension,
                            IsHiddenItem = isHidden,
                            Opacity = opacity,
                            FileImage = null,
                            LoadFileIcon = !(bool)response.Message["IsFolder"] && itemThumbnailImgVis,
                            LoadUnknownTypeGlyph = !(bool)response.Message["IsFolder"] && !isUrl && itemEmptyImgVis,
                            LoadFolderGlyph = (bool)response.Message["IsFolder"],
                            ItemName = itemName,
                            ItemDateModifiedReal = itemModifiedDate,
                            ItemDateAccessedReal = itemLastAccessDate,
                            ItemDateCreatedReal = itemCreatedDate,
                            ItemType = isUrl ? "ShortcutWebLinkFileType".GetLocalized() : "ShortcutFileType".GetLocalized(),
                            ItemPath = itemPath,
                            FileSize = itemSize,
                            FileSizeBytes = itemSizeBytes,
                            TargetPath = target,
                            Arguments = (string)response.Message["Arguments"],
                            WorkingDirectory = (string)response.Message["WorkingDirectory"],
                            RunAsAdmin = (bool)response.Message["RunAsAdmin"],
                            IsUrl = isUrl,
                            ContainsFilesOrFolders = containsFilesOrFolders
                        };
                    }
                }
            }
            else
            {
                bool isHidden = (((FileAttributes)findData.dwFileAttributes & FileAttributes.Hidden) == FileAttributes.Hidden);
                double opacity = 1;

                if (isHidden)
                {
                    opacity = 0.4;
                }

                return new ListedItem(null, dateReturnFormat)
                {
                    PrimaryItemAttribute = StorageItemTypes.File,
                    FileExtension = itemFileExtension,
                    LoadUnknownTypeGlyph = itemEmptyImgVis,
                    FileImage = null,
                    LoadFileIcon = itemThumbnailImgVis,
                    LoadFolderGlyph = itemFolderImgVis,
                    ItemName = itemName,
                    IsHiddenItem = isHidden,
                    Opacity = opacity,
                    ItemDateModifiedReal = itemModifiedDate,
                    ItemDateAccessedReal = itemLastAccessDate,
                    ItemDateCreatedReal = itemCreatedDate,
                    ItemType = itemType,
                    ItemPath = itemPath,
                    FileSize = itemSize,
                    FileSizeBytes = itemSizeBytes
                };
            }
            return null;
        }
    }
}
