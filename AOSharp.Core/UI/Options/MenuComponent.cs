namespace AOSharp.Core.UI.Options
{
    public abstract class MenuComponent
    {
        public readonly string Name;
        public readonly string DisplayName;

        public MenuComponent(string name, string displayName)
        {
            Name = name;
            DisplayName = displayName;
        }

        internal abstract View CreateView();
    }
}
