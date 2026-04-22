using System;
using System.Collections.Generic;
using System.IO;
using AOSharp.Common.Unmanaged.DataTypes;
using AOSharp.Common.Unmanaged.Interfaces;
using AOSharp.Core.UI;
using Newtonsoft.Json;

namespace AOSharp.Core
{
    public class Settings
    {
        private readonly string _groupName;
        private readonly string _savePath;
        private List<string> _variables = new List<string>();
        private Dictionary<string, string> _storedValues;

        public Variant this[string name]
        {
            get
            {
                if (!_variables.Contains(name))
                    return null;

                return DistributedValue.GetDValue($"{_groupName}__{name}", false);
            }

            set
            {
                DistributedValue.SetDValue($"{_groupName}__{name}", value);
            }
        }

        public Settings(string groupName)
        {
            _groupName = groupName;
            _savePath = $"{Preferences.GetCharacterPath()}\\AOSharp\\{groupName}.config";
            Load();
        }

        private void AddVariable(string name)
        {
            _variables.Add(name);

            if (_storedValues.ContainsKey(name))
                this[name] = Variant.LoadFromString(_storedValues[name]);
        }

        public void AddVariable(string name, int value)
        {
            DistributedValue.Create($"{_groupName}__{name}", value);
            AddVariable(name);
        }

        public void AddVariable(string name, float value)
        {
            DistributedValue.Create($"{_groupName}__{name}", value);
            AddVariable(name);
        }

        public void AddVariable(string name, bool value)
        {
            DistributedValue.Create($"{_groupName}__{name}", value);
            AddVariable(name);
        }

        public void DeleteVariable(string name)
        {
            _variables.Remove(name);
        }

        public void Save()
        {
            Dictionary<string, string> values = new Dictionary<string, string>();

            foreach(string variable in _variables)
                values.Add(variable, this[variable].ToString());

            new FileInfo(_savePath).Directory.Create(); //Create folder if it doesn't exist
            File.WriteAllText(_savePath, JsonConvert.SerializeObject(values, Formatting.Indented));
        }

        public void Load()
        {
            try
            {
                _storedValues = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(_savePath));

                if(_storedValues == null)
                    throw new Exception("JSON Object is null. Likely due to corrupt config file.");
            } 
            catch 
            {
                _storedValues = new Dictionary<string, string>();
            }
        }
    }
}
