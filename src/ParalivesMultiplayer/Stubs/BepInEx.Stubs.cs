using System;
using UnityEngine;

namespace BepInEx
{
    public abstract class BaseUnityPlugin : MonoBehaviour
    {
        public string Guid { get; protected set; } = "";
        public string Name { get; protected set; } = "";
        public Version Version { get; protected set; } = new Version();

        public BepInEx.Configuration.Config Config { get; protected set; } = new BepInEx.Configuration.Config();

        public BepInEx.Logging.ManualLogSource Logger { get; protected set; } = new BepInEx.Logging.ManualLogSource("");

        virtual protected void Awake() { }
        virtual protected void Update() { }
        virtual protected void OnDestroy() { }
        virtual protected void OnGUI() { }
    }
}

namespace BepInEx.Configuration
{
    public class Config
    {
        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description = "")
        {
            return new ConfigEntry<T>(defaultValue);
        }
    }

    public class ConfigEntry<T>
    {
        private T _value;
        public T Value
        {
            get => _value;
            set => _value = value;
        }

        public ConfigEntry(T defaultValue)
        {
            _value = defaultValue;
        }

        public static implicit operator T(ConfigEntry<T> entry) => entry.Value;
    }
}

namespace BepInEx.Logging
{
    public enum LogLevel
    {
        Fatal,
        Error,
        Warning,
        Message,
        Info,
        Debug
    }

    public struct LogEventArgs
    {
        public LogLevel Level;
        public string Source;
        public object Data;
    }

    public interface ILogListener
    {
        void LogMessage(string source, object message);
        void LogEvent(object sender, LogEventArgs eventArgs);
        void LogFatal(string source, object data);
        void LogError(string source, object data);
        void LogWarning(string source, object data);
        void LogMessage(LogEventArgs eventArgs);
        void LogInfo(string source, object data);
        void LogDebug(string source, object data);
    }

    public class ManualLogSource
    {
        public string Source { get; private set; }
        public event Action<LogEventArgs> Listened;

        public ManualLogSource(string source)
        {
            Source = source;
        }

        public void Log(LogLevel level, object data)
        {
            Listened?.Invoke(new LogEventArgs { Level = level, Source = Source, Data = data });
        }

        public void LogFatal(object data) => Log(LogLevel.Fatal, data);
        public void LogError(object data) => Log(LogLevel.Error, data);
        public void LogWarning(object data) => Log(LogLevel.Warning, data);
        public void LogMessage(object data) => Log(LogLevel.Message, data);
        public void LogInfo(object data) => Log(LogLevel.Info, data);
        public void LogDebug(object data) => Log(LogLevel.Debug, data);
    }
}
