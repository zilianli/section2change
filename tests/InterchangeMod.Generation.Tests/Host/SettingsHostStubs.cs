// Minimal host declarations for executing the REAL linked Setting.SetValue method.
// No game input, persistence, UI rendering or ECS behavior is emulated or claimed.
namespace Colossal
{
    public interface IDictionaryEntryError { }
    public interface IDictionarySource
    {IEnumerable<KeyValuePair<string,string>> ReadEntries(IList<IDictionaryEntryError> errors,Dictionary<string,int> counts);void Unload();}
}
namespace Colossal.IO.AssetDatabase
{
    public sealed class FileLocationAttribute(string name):System.Attribute { }
}
namespace Game.Modding { public interface IMod { } }
namespace Game.Input
{
    public enum ActionType {Button}
    public enum BindingKeyboard {I,Comma,Period}
    public readonly struct ProxyBinding { }
    public static class Usages {public const string kDefaultUsage="Default",kToolUsage="Tool";}
}
namespace Game.Settings
{
    public abstract class ModSetting(Game.Modding.IMod mod)
    {
        public abstract void SetDefaults();protected void ResetKeyBindings(){}
        public string GetSettingsLocaleID()=>"settings";
        public string GetOptionTabLocaleID(string key)=>"tab"+key;
        public string GetOptionGroupLocaleID(string key)=>"group"+key;
        public string GetOptionLabelLocaleID(string key)=>"label"+key;
        public string GetOptionDescLocaleID(string key)=>"description"+key;
        public string GetBindingKeyLocaleID(string key)=>"binding"+key;
        public string GetBindingMapLocaleID()=>"bindingMap";
    }
    public sealed class SettingsUIHiddenAttribute:System.Attribute { }
    public sealed class SettingsUIGroupOrderAttribute(params string[] groups):System.Attribute { }
    public sealed class SettingsUIShowGroupNameAttribute(params string[] groups):System.Attribute { }
    [System.AttributeUsage(System.AttributeTargets.Class,AllowMultiple=true)]
    public sealed class SettingsUIKeyboardActionAttribute(string name,Game.Input.ActionType type,string[] usages):System.Attribute { }
    public sealed class SettingsUIKeyboardBindingAttribute(Game.Input.BindingKeyboard key,string name,bool shift=false):System.Attribute { }
    public sealed class SettingsUISectionAttribute(string section,string group):System.Attribute { }
}
namespace InterchangeMod
{
    internal static class Mod
    {
        internal const string kToggleToolActionName="Toggle",kRotateLeftActionName="Left",kRotateRightActionName="Right";
        internal static readonly SettingsTestLog log=new();
    }
    internal sealed class SettingsTestLog {public void Info(string value){} }
}
