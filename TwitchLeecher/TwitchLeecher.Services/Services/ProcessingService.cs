using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using TwitchLeecher.Core.Models;
using TwitchLeecher.Services.Interfaces;
using TwitchLeecher.Shared.Helpers;
using TwitchLeecher.Shared.IO;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System.Threading;

namespace TwitchLeecher.Services.Services
{
    internal class ProcessingService : IProcessingService
    {
        #region Constants

        public string FFMPEGExe =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? "ffmpeg"
                : Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "ffmpeg.exe");

        #endregion Constants

        #region Properties

        #endregion Properties

        #region Methods

        public void ConcatParts(Action<string> log, Action<string> setStatus, Action<double> setProgress,
            TwitchPlaylist vodPlaylist, string concatFile)
        {
            setStatus("Merging files");
            setProgress(0);

            log(Environment.NewLine + Environment.NewLine + "Merging all VOD parts into '" + concatFile + "'...");

            using (FileStream outputStream = new FileStream(concatFile, FileMode.OpenOrCreate, FileAccess.Write))
            {
                int partsCount = vodPlaylist.Count;

                for (int i = 0; i < partsCount; i++)
                {
                    TwitchPlaylistPart part = vodPlaylist[i];

                    if (!File.Exists(part.LocalFile))
                    {
                        continue;
                    }

                    using (FileStream partStream = new FileStream(part.LocalFile, FileMode.Open, FileAccess.Read))
                    {
                        int maxBytes;
                        byte[] buffer = new byte[4096];

                        while ((maxBytes = partStream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            outputStream.Write(buffer, 0, maxBytes);
                        }
                    }

                    FileSystem.DeleteFile(part.LocalFile);

                    setProgress(i * 100 / partsCount);
                }
            }

            setProgress(100);
        }


        public void ConvertVideo(Action<string> log, Action<string> setStatus, Action<double> setProgress,
            Action<bool> setIsIndeterminate, string sourceFile, string outputFile, CropInfo cropInfo)
        {
            setStatus("Converting Video");
            setIsIndeterminate(true);

            log(Environment.NewLine + Environment.NewLine + "Executing '" + FFMPEGExe + "' on '" + sourceFile + "'...");

            ProcessStartInfo psi = new ProcessStartInfo(FFMPEGExe)
            {
                Arguments = "-y" +
                            (cropInfo.CropStart
                                ? " -ss " + cropInfo.Start.ToString(CultureInfo.InvariantCulture)
                                : null) + " -i \"" + sourceFile + "\" -analyzeduration " + int.MaxValue +
                            " -probesize " + int.MaxValue + " -c:v copy -c:a copy" +
                            (cropInfo.CropEnd
                                ? " -t " + cropInfo.Length.ToString(CultureInfo.InvariantCulture)
                                : null) + " \"" + outputFile + "\"",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                StandardErrorEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            log(Environment.NewLine + "Command line arguments: " + psi.Arguments + Environment.NewLine);

            using (Process p = new Process())
            {
                FixedSizeQueue<string> logQueue = new FixedSizeQueue<string>(200);

                TimeSpan duration = TimeSpan.FromSeconds(cropInfo.Length);

                DataReceivedEventHandler outputDataReceived = new DataReceivedEventHandler((s, e) =>
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(e.Data))
                        {
                            string dataTrimmed = e.Data.Trim();

                            logQueue.Enqueue(dataTrimmed);

                            if (dataTrimmed.StartsWith("frame", StringComparison.OrdinalIgnoreCase) &&
                                duration != TimeSpan.Zero)
                            {
                                string timeStr = dataTrimmed.Substring(dataTrimmed.IndexOf("time") + 4).Trim();
                                timeStr = timeStr.Substring(timeStr.IndexOf("=") + 1).Trim();
                                timeStr = timeStr.Substring(0, timeStr.IndexOf(" ")).Trim();

                                if (TimeSpan.TryParse(timeStr, out TimeSpan current))
                                {
                                    setIsIndeterminate(false);
                                    setProgress(current.TotalMilliseconds / duration.TotalMilliseconds * 100);
                                }
                                else
                                {
                                    setIsIndeterminate(true);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        log(Environment.NewLine + "An error occured while reading '" + FFMPEGExe + "' output stream!" +
                            Environment.NewLine + Environment.NewLine + ex.ToString());
                    }
                });

                p.OutputDataReceived += outputDataReceived;
                p.ErrorDataReceived += outputDataReceived;
                p.StartInfo = psi;
                p.Start();
                p.BeginErrorReadLine();
                p.BeginOutputReadLine();
                p.WaitForExit();

                if (p.ExitCode == 0)
                {
                    log(Environment.NewLine + "Video conversion complete!");
                }
                else
                {
                    if (!logQueue.IsEmpty)
                    {
                        foreach (string line in logQueue)
                        {
                            log(Environment.NewLine + line);
                        }
                    }

                    throw new ApplicationException("An error occured while converting the video!");
                }
            }
        }
        public void ZipFile(Action<string> log, Action<string> setStatus, Action<double> setProgress, Action<bool> setIsIndeterminate, string sourceFile, string outputFile)
        {
            setStatus("Zipping file");
            setProgress(0);
            setIsIndeterminate(true);

            log(Environment.NewLine + Environment.NewLine + "Zipping '" + sourceFile + "' to '" + outputFile + "'...");

            using (FileStream zipToOpen = new FileStream(outputFile, FileMode.Create))
            {
                using (ZipArchive archive = new ZipArchive(zipToOpen, ZipArchiveMode.Create))
                {
                    ZipArchiveEntry entry = archive.CreateEntryFromFile(sourceFile, Path.GetFileName(sourceFile), CompressionLevel.Optimal);
                }
            }

            setProgress(1.0);
            setIsIndeterminate(false);
        }

        public (string DownloadUrl, string DeleteKey) UploadToGigafileBin(Action<string> log, Action<string> setStatus, Action<double> setProgress, CancellationToken cancellationToken, bool requireHeadless, string sourceFile)
        {
            setStatus("Uploading file to Gigafile Bin");
            setProgress(0);

            var download_url = "";
            var del_key = "";

            log(Environment.NewLine + Environment.NewLine + "Initiating Chrome WebDriver for gigafile.nu");

            ChromeDriver chrome = null;
            try
            {
                var service = ChromeDriverService.CreateDefaultService();
                service.HideCommandPromptWindow = true;
                if (requireHeadless)
                {
                    var options = new ChromeOptions();
                    options.AddArgument("--headless");

                    chrome = new ChromeDriver(service, options);
                }
                else
                {
                    chrome = new ChromeDriver(service);
                }
                chrome.Url = "https://gigafile.nu/";

                var wait = new WebDriverWait(chrome, TimeSpan.FromSeconds(10));
                var lifetime_element = wait.Until(d => d.FindElement(By.CssSelector("li[data-lifetime-val='100']")));
                cancellationToken.ThrowIfCancellationRequested();
                chrome.FindElement(By.CssSelector("li[data-lifetime-val='3']"));
                chrome.FindElement(By.CssSelector("li[data-lifetime-val='5']"));
                chrome.FindElement(By.CssSelector("li[data-lifetime-val='7']"));
                chrome.FindElement(By.CssSelector("li[data-lifetime-val='14']"));
                chrome.FindElement(By.CssSelector("li[data-lifetime-val='30']"));
                chrome.FindElement(By.CssSelector("li[data-lifetime-val='60']"));
                cancellationToken.ThrowIfCancellationRequested();

                var jsexecutor = (IJavaScriptExecutor)chrome;
                jsexecutor.ExecuteScript("arguments[0].click();", lifetime_element);

                var file_input_element = wait.Until(d => d.FindElement(By.CssSelector("#upload_panel_button > input")));
                cancellationToken.ThrowIfCancellationRequested();
                file_input_element.SendKeys(sourceFile);

                var last_progress_text = "";
                while (true)
                {
                    var progress_text = chrome.FindElement(By.CssSelector("#file_0 > div.file_info_prog_box > span")).Text;
                    if (last_progress_text != progress_text)
                    {
                        if (progress_text == "完了！")
                        {
                            break;
                        }

                        int progressPercentage = 0;
                        if (int.TryParse(progress_text.TrimEnd('%'), out progressPercentage))
                        {
                            setProgress(progressPercentage);
                        }

                        last_progress_text = progress_text;
                    }
                    Thread.Sleep(1);
                }

                var download_text_element = wait.Until(d => d.FindElement(By.CssSelector("#file_0 > div.file_info_url_box.clearfix > input.file_info_url.url")));
                download_url = download_text_element.GetAttribute("origin");

                var delkey_text_element = chrome.FindElement(By.CssSelector("#file_0 > div.file_info_url_box.clearfix > span.file_info_url.file_info_url_delkey > input.delkey"));
                del_key = delkey_text_element.GetAttribute("origin");

                setProgress(100);
                /*
                var matomete_link_btn = chrome.FindElement(By.Id("matomete_btn"));
                jsexecutor.ExecuteScript("arguments[0].click();", matomete_link_btn);

                var alert = wait.Until(d => d.SwitchTo().Alert());
                var t = alert.Text;
                alert.Accept();

                var matomete_url_element = chrome.FindElement(By.Id("matomete_url"));
                var origin_value = matomete_url_element.GetAttribute("origin");
                var a = matomete_url_element.GetAttribute("href");
                */
            }
            catch (Exception ex)
            {
                log(Environment.NewLine + "An error occured while uploading to gigafile.nu!" +
                    Environment.NewLine + Environment.NewLine + ex.ToString());
            }
            finally
            {
                if (chrome != null)
                {
                    chrome.Quit();
                }
            }

            return (DownloadUrl: download_url, DeleteKey: del_key);
        }


        #endregion Methods
    }
}