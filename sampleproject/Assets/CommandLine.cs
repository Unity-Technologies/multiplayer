using System;
using System.ComponentModel;
using UnityEngine;

public class CommandLine
{
    public static bool TryGetCommandLineArgValue<T>(string argName, out T value)
    {
        value = default(T);
        try
        {
            TypeConverter converter = TypeDescriptor.GetConverter(typeof(T));
            if (!converter.CanConvertFrom(typeof(string)))
                return false;

            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Compare(args[i], argName, StringComparison.InvariantCultureIgnoreCase) != 0 ||
                    args.Length <= i + 1)
                    continue;

                value = (T) converter.ConvertFromString(args[i + 1]);
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}
