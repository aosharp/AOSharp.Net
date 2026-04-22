using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AOSharp.Core.Logging
{
    public enum LogLevel
    {
        Verbose = LogEventLevel.Verbose,
        Debug = LogEventLevel.Debug,
        Information = LogEventLevel.Information,
        Warning = LogEventLevel.Warning,
        Error = LogEventLevel.Error,
        Fatal = LogEventLevel.Fatal,
        Off = LogEventLevel.Fatal + 1
    }
}
