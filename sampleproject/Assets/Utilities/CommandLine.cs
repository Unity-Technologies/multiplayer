using System;
using System.ComponentModel;
using UnityEngine;

public class CommandLine
{
    static string[] s_Args;

    // Try to get value from arg - will populate [value] with default() if not found
    public static bool TryGetCommandLineArgValue<T>(string argName, out T value)
    {
        if (s_Args == null) s_Args = Environment.GetCommandLineArgs();

        value = default(T);

        try
        {
            TypeConverter converter = TypeDescriptor.GetConverter(typeof(T));

            if (!converter.CanConvertFrom(typeof(string)))
                return false;

            for (int i = 0; i < s_Args.Length; i++)
            {
                if (string.Compare(s_Args[i], argName, StringComparison.InvariantCultureIgnoreCase) != 0 ||
                    s_Args.Length <= i + 1)
                    continue;

                value = (T)converter.ConvertFromString(s_Args[i + 1]);
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    // Non-destructive version of TryGetCommandLineArgValue() - does not change value of [varToUpdate] if argument not found
    public static bool TryUpdateVariableWithArgValue<T>(ref T varToUpdate, string arg)
    {
        if (CommandLine.TryGetCommandLineArgValue(arg, out T argValue))
        {
            varToUpdate = argValue;
            return true;
        }

        return false;
    }

    // Returns true if command line flag found, false if not found
    public static bool HasArgument(string argName)
    {
        if (s_Args == null) s_Args = Environment.GetCommandLineArgs();

        for (int i = 0, length = s_Args.Length; i < length; i++)
        {
            if (s_Args[i] != null)
            {
                if (string.Equals(argName, s_Args[i], StringComparison.InvariantCultureIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    public static void PrintArgsToLog()
    {
        Debug.Log("Launch args: " + Environment.CommandLine);
    }
}
