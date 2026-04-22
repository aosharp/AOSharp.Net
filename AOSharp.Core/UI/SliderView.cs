using System;
using AOSharp.Common.Unmanaged.Imports;
using AOSharp.Common.Unmanaged.DataTypes;

namespace AOSharp.Core.UI
{
    public class SliderView : View
    {
        public float Value
        {
            get => GetValue();
            set => SetValue(value);
        }

        protected SliderView(IntPtr pointer, bool track = false) : base(pointer, track)
        {
        }

        public void SetValue(float value)
        {
            SliderView_c.SetValue(Pointer, Variant.Create(value).Pointer, true);
        }

        public float GetValue()
        {
            Variant pOutput = Variant.Create(false);
            SliderView_c.GetValue(_pointer, pOutput.Pointer);
            float result = pOutput.AsFloat();
            pOutput.Dispose();

            return result;
        }

        public override void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}
