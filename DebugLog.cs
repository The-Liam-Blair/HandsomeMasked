using BepInEx.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace HandsomeMasked
{
    internal static class DebugLog
    {
        private static ManualLogSource _logger;

        internal static void Init(ManualLogSource logger)
        {
            _logger = logger;
        }

        internal static void Log(object data) => _logger.LogInfo(data);
    }
}
