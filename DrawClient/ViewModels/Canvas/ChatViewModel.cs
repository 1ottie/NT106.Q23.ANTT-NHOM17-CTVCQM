using DrawClient.Models;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace DrawClient.ViewModels.Canvas
{
    public class ChatViewModel
    {
        public ObservableCollection<ChatMessage> ChatMessages
        {
            get;
            set;
        }

        public ChatViewModel()
        {
            ChatMessages = new ObservableCollection<ChatMessage>();

            // Test data
            AddMessage("Nguyen Van A", "Hello mọi người!");
            AddMessage("Tran Thi B", "Xin chào 👋");
        }

        public void AddMessage(string user, string message)
        {
            DateTime now = DateTime.Now;

            bool showSeparator = false;

            if (ChatMessages.Count == 0)
            {
                showSeparator = true;
            }
            else
            {
                ChatMessage lastMessage = ChatMessages.Last();

                bool differentDay =
                    lastMessage.Timestamp.Date != now.Date;

                bool over15Minutes =
                    (now - lastMessage.Timestamp).TotalMinutes >= 15;

                if (differentDay || over15Minutes)
                {
                    showSeparator = true;
                }
            }

            ChatMessages.Add(new ChatMessage
            {
                User = user,
                Message = message,
                Timestamp = now,
                ShowSeparator = showSeparator
            });
        }
    }
}