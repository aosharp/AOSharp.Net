using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AOSharp.Core.UI;
using AOSharp.Common.GameData;
using Serilog.Formatting.Display;
using Serilog.Formatting;
using System.IO;

namespace AOSharp.Core.Logging
{
    public static class ChatSinkExtensions
    {
        public static LoggerConfiguration Chat(
            this LoggerSinkConfiguration sinkConfiguration,
            LogEventLevel restrictedToMinimumLevel = LogEventLevel.Verbose,
            string outputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
            IFormatProvider formatProvider = null,
            LoggingLevelSwitch levelSwitch = null)
        {
            if (sinkConfiguration == null) throw new ArgumentNullException(nameof(sinkConfiguration));
            if (outputTemplate == null) throw new ArgumentNullException(nameof(outputTemplate));

            var formatter = new MessageTemplateTextFormatter(outputTemplate, formatProvider);

            return sinkConfiguration.Chat(formatter, restrictedToMinimumLevel, levelSwitch);
        }

        public static LoggerConfiguration Chat(
            this LoggerSinkConfiguration sinkConfiguration,
            ITextFormatter formatter,
            LogEventLevel restrictedToMinimumLevel = LogEventLevel.Verbose,
            LoggingLevelSwitch levelSwitch = null)
        {
            if (sinkConfiguration == null) throw new ArgumentNullException(nameof(sinkConfiguration));
            if (formatter == null) throw new ArgumentNullException(nameof(formatter));

            return sinkConfiguration.Sink(new ChatSink(formatter), restrictedToMinimumLevel, levelSwitch);
        }
    }

    public class ChatSink : ILogEventSink
    {
        private readonly IFormatProvider _formatProvider;

        readonly ITextFormatter _formatter;


        public ChatSink(ITextFormatter formatter)
        {
            _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        }

        public void Emit(LogEvent logEvent)
        {
            string formattedMessage;
            using (var buffer = new StringWriter())
            {
                _formatter.Format(logEvent, buffer);
                formattedMessage = buffer.ToString();
            }

            ChatColor messageColor = ChatColor.Gold;

            switch (logEvent.Level)
            {
                case LogEventLevel.Debug:
                case LogEventLevel.Verbose:
                    messageColor = ChatColor.LightBlue;
                    break;
                case LogEventLevel.Information:
                    messageColor = ChatColor.Gold;
                    break;
                case LogEventLevel.Warning:
                    messageColor = ChatColor.Orange;
                    break;
                case LogEventLevel.Error:
                case LogEventLevel.Fatal:
                    messageColor = ChatColor.Red;
                    break;
            }

            Chat.WriteLine(formattedMessage, messageColor);
        }
    }
}
