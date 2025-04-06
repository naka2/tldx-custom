using System;
using System.Threading;
using TwitchLeecher.Core.Models;

namespace TwitchLeecher.Services.Interfaces
{
    public interface IProcessingService
    {
        string FFMPEGExe { get; }

        void ConcatParts(Action<string> log, Action<string> setStatus, Action<double> setProgress, TwitchPlaylist vodPlaylist, string concatFile);

        void ConvertVideo(Action<string> log, Action<string> setStatus, Action<double> setProgress, Action<bool> setIsIndeterminate, string sourceFile, string outputFile, CropInfo cropInfo);

        void ZipFile(Action<string> log, Action<string> setStatus, Action<double> setProgress, Action<bool> setIsIndeterminate, string sourceFile, string outputFile);

        (string DownloadUrl, string DeleteKey) UploadToGigafileBin(Action<string> log, Action<string> setStatus, Action<double> setProgress, CancellationToken cancellationToken, bool requireHeadless, string sourceFile);
    }
}