using RimSharp.MyApp.AppFiles;

namespace RimSharp.Features.WorkshopDownloader.Models
{
    public class DownloadItem : ViewModelBase
    {
        private string _name;
        private string _url;
        private string _steamId;
        private string _publishDate;
        private string _standardDate;

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string Url
        {
            get => _url;
            set => SetProperty(ref _url, value);
        }

        public string SteamId
        {
            get => _steamId;
            set => SetProperty(ref _steamId, value);
        }

        public string PublishDate
        {
            get => _publishDate;
            set => SetProperty(ref _publishDate, value);
        }

        public string StandardDate
        {
            get => _standardDate;
            set => SetProperty(ref _standardDate, value);
        }
    }
}
