using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace DrawClient.ViewModels
{
    public class UserParticipant
    {
        public string Initials { get; set; }
        public string ColorHex { get; set; }
    }

    public class ChatMessage
    {
        public string User { get; set; }
        public string Message { get; set; }
        public string Time { get; set; }
    }

    public class WhiteboardViewModel : INotifyPropertyChanged
    {
        // Danh sách dữ liệu
        public ObservableCollection<UserParticipant> Users { get; set; }
        public ObservableCollection<string> NetworkLogs { get; set; }
        public ObservableCollection<ChatMessage> ChatMessages { get; set; }

        // Các State quản lý Giao diện
        private bool _isSidebarOpen = true;
        public bool IsSidebarOpen
        {
            get => _isSidebarOpen;
            set { _isSidebarOpen = value; OnPropertyChanged(); }
        }

        private int _playbackProgress = 0;
        public int PlaybackProgress
        {
            get => _playbackProgress;
            set { _playbackProgress = value; OnPropertyChanged(); }
        }

        private string _activeTool = "Pen";
        public string ActiveTool
        {
            get => _activeTool;
            set { _activeTool = value; OnPropertyChanged(); }
        }

        public WhiteboardViewModel()
        {
            // Mock Data
            Users = new ObservableCollection<UserParticipant>
            {
                new UserParticipant { Initials = "SC", ColorHex = "#1A73E8" },
                new UserParticipant { Initials = "MJ", ColorHex = "#34A853" },
                new UserParticipant { Initials = "ED", ColorHex = "#FBBC04" }
            };

            NetworkLogs = new ObservableCollection<string>
            {
                "14:23:45 → Encrypted packet sent",
                "14:23:46 ← Sync received from client 2",
                "14:23:48 ← User 'Mike' joined"
            };

            ChatMessages = new ObservableCollection<ChatMessage>
            {
                new ChatMessage { User = "Sarah Chen", Message = "alooo", Time = "14:20" },
                new ChatMessage { User = "Mike Johnson", Message = "aaalo", Time = "14:21" }
            };
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}