using AOSharp.Common.Unmanaged.Imports;
using AOSharp.Common.Unmanaged.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AOSharp.Core.UI
{
    public class UIController
    {
        public static EventHandler<View> ViewDeleted;
        public static EventHandler<Window> WindowDeleted;
        private static Dictionary<int, View> _trackedViews = new Dictionary<int, View>();
        private static List<Window> _windows = new List<Window>();

        internal static void RegisterView(View view)
        {
            if (_trackedViews.ContainsKey(view.Handle))
                return;

            _trackedViews.Add(view.Handle, view);
        }

        internal static void UnregisterView(View view)
        {
            _trackedViews.Remove(view.Handle);
        }

        internal static void AddWindow(Window window)
        {
            _windows.Add(window);
        }

        internal static bool FindWindow(string name, out Window window)
        {
            return (window = _windows.FirstOrDefault(x => x.Name == name)) != null;
        }

        internal static bool FindViewByPointer<T>(IntPtr pointer, out T view) where T : View
        {
            return (view = _trackedViews.Values.FirstOrDefault(x => x.Pointer == pointer) as T) != null;
        }

        internal static void UpdateViews()
        {
            try
            {
                foreach(View view in _trackedViews.Values)
                {
                    view.Update();
                }
            }
            catch (Exception e)
            {
                Chat.WriteLine($"This shouldn't happen pls report (UIController): {e.Message}");
            }
        }

        internal static void Cleanup()
        {
            foreach(Window window in _windows)
                window.Close();
        }

        private static void OnCheckBoxToggled(IntPtr pCheckBox, bool enabled)
        {
            Checkbox checkBox = _trackedViews.Values.FirstOrDefault(x => x is Checkbox && x.Pointer == pCheckBox) as Checkbox;

            if (checkBox == null)
                return;

            checkBox.Toggled?.Invoke(checkBox, enabled);
        }

        private static void OnButtonPressed(IntPtr pButton)
        {
            ButtonBase button = _trackedViews.Values.FirstOrDefault(x => x is ButtonBase && x.Pointer == pButton) as ButtonBase;

            if (button == null)
                return;

            button.Clicked?.Invoke(null, button);
        }

        private static void OnViewDeleted(IntPtr pView)
        {
            View view = new View(pView, false);

            if (!_trackedViews.ContainsKey(view.Handle))
                return;

            ViewDeleted?.Invoke(null, view);
            _trackedViews.Remove(view.Handle);
        }

        private static void OnWindowDeleted(IntPtr pWindow)
        {
            Window window = _windows.FirstOrDefault(x => x.Pointer == pWindow);

            if (window == null)
                return;

            WindowDeleted?.Invoke(null, window);
            window.IsValid = false;
            _windows.Remove(window);
        }

        private static void OnMultiListViewItemStateChanged(IntPtr pItem, bool selected)
        {
            MultiListView listView = MultiListViewItem.GetListViewForPointer(pItem);

            if(listView == null)
                return;

            listView.OnItemSelectionStateChanged(pItem, selected);
        }

        private static int OnDynamicIDResolve(string name)
        {
            if (DynamicID.DynamicIDOverrides.TryGetValue(name, out int id))
                return id;

            return 0;
        }
    }
}
