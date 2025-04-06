using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using DynamicData.Binding;
using ReactiveUI;
using TwitchLeecher.Core.Models;
using TwitchLeecher.Gui.Interfaces;
using TwitchLeecher.Gui.Services;
using TwitchLeecher.Gui.Types;
using TwitchLeecher.Services.Interfaces;
using TwitchLeecher.Shared.Commands;
using TwitchLeecher.Shared.Extensions;

namespace TwitchLeecher.Gui.ViewModels
{
    public partial class BulkDownloadViewModel : ViewModelBase
    {
        #region Fields
        private readonly IDownloadService _downloadService;
        private readonly INavigationService _navigationService;
        ObservableCollection<DownloadParameters> _paramsArray;
        bool _doGenerateDownloadSummaryCSV;
        bool _doZipDownloadFile;
        bool _doUploadToGigafileBinAfterDownload;
        bool _isChromeDriverHeadless;
        ICommand _startDownloadCommand;
        #endregion Fields

        #region Constructors
        public BulkDownloadViewModel(IDownloadService downloadService, INavigationService navigationService)
        {
            _downloadService = downloadService;
            _navigationService = navigationService;
        }
        #endregion Constructors

        #region Properties
        public ICommand StartDownloadCommand
        {
            get
            {
                if (_startDownloadCommand == null)
                {
                    _startDownloadCommand = new DelegateCommand(StartDownloadVideo);
                }

                return _startDownloadCommand;
            }
        }

        public ObservableCollection<DownloadParameters> ParamsArray
        {
            get
            {
                return _paramsArray;
            }
            set
            {
                SetProperty(ref _paramsArray, value, nameof(ParamsArray));
            }
        }

        public bool DoGenerateDownloadSummaryCSV
        {
            get { return _doGenerateDownloadSummaryCSV; }
            set
            {
                SetProperty(ref _doGenerateDownloadSummaryCSV, value, nameof(DoGenerateDownloadSummaryCSV));
            }
        }

        public bool DoZipDownloadFile
        {
            get { return _doZipDownloadFile; }
            set
            {
                SetProperty(ref _doZipDownloadFile, value, nameof(DoZipDownloadFile));
            }
        }

        public bool DoUploadToGigafileBinAfterDownload
        {
            get { return _doUploadToGigafileBinAfterDownload; }
            set
            {
                SetProperty(ref _doUploadToGigafileBinAfterDownload, value, nameof(DoUploadToGigafileBinAfterDownload));
            }
        }

        public bool IsChromeDriverHeadless
        {
            get { return _isChromeDriverHeadless; }
            set
            {
                SetProperty(ref _isChromeDriverHeadless, value, nameof(IsChromeDriverHeadless));
            }
        }
        #endregion Properties

        #region Methods

        public void StartDownloadVideo()
        {
            foreach (var downloadParameters in ParamsArray)
            {
                if (_doZipDownloadFile)
                {
                    downloadParameters.DoZipAfterDownload = true;
                }

                if (_doGenerateDownloadSummaryCSV)
                {
                    downloadParameters.CsvFilePathDownloadSummary = Path.Combine(
                        downloadParameters.Folder, "_DownloadSummary.csv");
                }

                if (_doUploadToGigafileBinAfterDownload)
                {
                    downloadParameters.DoUploadToGigafileBinAfterDownload = true;
                }

                if (_isChromeDriverHeadless)
                {
                    downloadParameters.RunChromeDriverHeadless = true;
                }

                _downloadService.Enqueue(downloadParameters);
            }

            _navigationService.ShowDownloads();
        }


        #endregion Methods

        #region EventHandlers
        #endregion EventHandlers
    }
}