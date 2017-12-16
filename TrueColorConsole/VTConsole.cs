﻿using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using JetBrains.Annotations;

namespace TrueColorConsole
{
    /// <summary>
    ///     Represents a wrapper of virtual terminal sequences for <see cref="Console" />, see
    ///     https://docs.microsoft.com/en-us/windows/console/console-virtual-terminal-sequences for more details.
    /// </summary>
    public static partial class VTConsole
    {
        #region Interop

        private const uint StdOutputHandle = unchecked((uint) -11);
        private const uint StdInputHandle = unchecked((uint) -10);
        private static readonly IntPtr InvalidHandleValue = new IntPtr(-1);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(uint nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        private static bool GetConsoleMode(IntPtr hConsoleHandle, out ConsoleModeOutput mode)
        {
            if (!GetConsoleMode(hConsoleHandle, out uint lpMode))
            {
                mode = 0;
                return false;
            }

            mode = (ConsoleModeOutput) lpMode;
            return true;
        }

        private static bool GetConsoleMode(IntPtr hConsoleHandle, out ConsoleModeInput mode)
        {
            if (!GetConsoleMode(hConsoleHandle, out uint lpMode))
            {
                mode = 0;
                return false;
            }

            mode = (ConsoleModeInput) lpMode;
            return true;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WriteConsole(
            IntPtr hConsoleOutput,
            [MarshalAs(UnmanagedType.LPArray)] byte[] lpBuffer,
            int lpNumberOfCharsToWrite,
            out int lpNumberOfCharsToWritten,
            IntPtr lpReserved
        );

        private static bool GetStdIn(out IntPtr handle)
        {
            handle = GetStdHandle(StdInputHandle);
            return handle != InvalidHandleValue;
        }

        private static bool GetStdOut(out IntPtr handle)
        {
            handle = GetStdHandle(StdOutputHandle);
            return handle != InvalidHandleValue;
        }

        #endregion

        #region General

        private const string ESC = "\x1b";
        private const string BEL = "\x07";
        private const string SUB = "\x1a";
        private const string DEL = "\x7f";

        private static readonly string[] BytesMap =
            Enumerable.Range(0, 256).Select(s => s.ToString()).ToArray();

        private static IntPtr _inHandle;
        private static ConsoleModeInput _inLast;
        private static IntPtr _outHandle;
        private static ConsoleModeOutput _outLast;

        /// <summary>
        ///     Gets if virtual terminal features are enabled.
        /// </summary>
        [PublicAPI]
        public static bool IsEnabled { get; private set; }

        /// <summary>
        ///     Gets if virtual terminal features are supported.
        /// </summary>
        [PublicAPI]
        public static bool IsSupported { get; } = Enable() && Disable();

        /// <summary>
        ///     Gets the handle to the console standard input.
        /// </summary>
        [PublicAPI]
        public static IntPtr StdIn => _inHandle;

        /// <summary>
        ///     Gets the handle to the console standard output.
        /// </summary>
        [PublicAPI]
        public static IntPtr StdOut => _outHandle;

        /// <summary>
        ///     Enables virtual terminal features.
        /// </summary>
        /// <returns>
        ///     <c>true</c> on success.
        /// </returns>
        [PublicAPI]
        public static bool Enable()
        {
            if (IsEnabled)
                return true;

            bool EnableInput()
            {
                if (!GetStdIn(out _inHandle))
                    return false;

                if (!GetConsoleMode(StdIn, out _inLast))
                    return false;

                var mode = _inLast | ConsoleModeInput.EnableVirtualTerminalInput;

                return SetConsoleMode(StdIn, (uint) mode);
            }

            bool EnableOutput()
            {
                if (!GetStdOut(out _outHandle))
                    return false;

                if (!GetConsoleMode(StdOut, out _outLast))
                    return false;

                var mode = _outLast | ConsoleModeOutput.EnableVirtualTerminalProcessing |
                           ConsoleModeOutput.DisableNewlineAutoReturn;

                if (SetConsoleMode(StdOut, (uint) mode))
                    return true;

                mode = _outLast | ConsoleModeOutput.EnableVirtualTerminalProcessing;

                return SetConsoleMode(StdOut, (uint) mode);
            }

            IsEnabled = EnableInput() && EnableOutput();

            return IsEnabled;
        }

        /// <summary>
        ///     Disables virtual terminal features.
        /// </summary>
        /// <returns>
        ///     <c>true</c> on success.
        /// </returns>
        [PublicAPI]
        public static bool Disable()
        {
            if (!IsEnabled)
                return false;

            bool DisableInput()
            {
                return GetStdIn(out var handle) && SetConsoleMode(handle, (uint) _inLast);
            }

            bool DisableOutput()
            {
                return GetStdOut(out var handle) && SetConsoleMode(handle, (uint) _outLast);
            }

            IsEnabled = !(DisableInput() && DisableOutput());

            _inHandle = IntPtr.Zero;
            _outHandle = IntPtr.Zero;
            _cursorKeysMode = VTCursorKeysMode.Normal;
            _keypadMode = VTKeypadMode.Numeric;

            return !IsEnabled;
        }

        /// <summary>
        ///     Gets the virtual terminal sequence for a background color.
        /// </summary>
        /// <param name="r"></param>
        /// <param name="g"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        [PublicAPI]
        public static string GetColorBackgroundString(int r, int g, int b)
        {
            return string.Concat(ESC, "[48;2;", BytesMap[r], ";", BytesMap[g], ";", BytesMap[b], "m");
        }

        /// <summary>
        ///     Gets the virtual terminal sequence for a foreground color.
        /// </summary>
        /// <param name="r"></param>
        /// <param name="g"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        [PublicAPI]
        public static string GetColorForegroundString(int r, int g, int b)
        {
            return string.Concat(ESC, "[38;2;", BytesMap[r], ";", BytesMap[g], ";", BytesMap[b], "m");
        }

        /// <summary>
        ///     Sets the background color for subsequent write calls.
        /// </summary>
        /// <param name="color">
        ///     Character background color.
        /// </param>
        [PublicAPI]
        public static void SetColorBackground(Color color)
        {
            Console.Write(GetColorBackgroundString(color.R, color.G, color.B));
        }

        /// <summary>
        ///     Sets the foreground color for subsequent write calls.
        /// </summary>
        /// <param name="color">
        ///     Character foreground color, i.e. text color.
        /// </param>
        [PublicAPI]
        public static void SetColorForeground(Color color)
        {
            Console.Write(GetColorForegroundString(color.R, color.G, color.B));
        }

        /// <summary>
        ///     Sets the formatting options for subsequent write calls.
        /// </summary>
        /// <param name="formats">
        ///     An array of formatting options to apply, 16 at most. Competing options will result in the last-most option taking
        ///     precedence.
        /// </param>
        [PublicAPI]
        public static void SetFormat(params VTFormat[] formats)
        {
            if (formats == null || !formats.Any())
                return;

            Console.Write($"{ESC}[{string.Join(";", formats.Take(16).Select(s => (int) s))}m");
        }

        /// <summary>
        ///     Fast writing using WriteConsole.
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns>
        ///     Number of characters written.
        /// </returns>
        [PublicAPI]
        public static int WriteFast(byte[] buffer)
        {
            WriteConsole(StdOut, buffer, buffer.Length, out var written, IntPtr.Zero);
            return written;
        }

        /// <summary>
        ///     Writes the current line terminator to the standard output.
        /// </summary>
        [PublicAPI]
        public static void WriteLine()
        {
            Console.WriteLine();
        }

        /// <summary>
        ///     Write the specified string value, followed by the current line terminator to the standard output.
        /// </summary>
        /// <param name="value">
        ///     The value to write.
        /// </param>
        [PublicAPI]
        public static void WriteLine(string value)
        {
            Console.WriteLine(value);
        }

        /// <summary>
        ///     Write the specified string value, followed by the current line terminator to the standard output.
        /// </summary>
        /// <param name="value">
        ///     The value to write.
        /// </param>
        /// <param name="foreground">
        ///     The color for the text.
        /// </param>
        [PublicAPI]
        public static void WriteLine(string value, Color foreground)
        {
            SetColorForeground(foreground);
            WriteLine(value);
        }

        /// <summary>
        ///     Write the specified string value, followed by the current line terminator to the standard output.
        /// </summary>
        /// <param name="value">
        ///     The value to write.
        /// </param>
        /// <param name="foreground">
        ///     The color for the text.
        /// </param>
        /// <param name="background">
        ///     The color for the background.
        /// </param>
        [PublicAPI]
        public static void WriteLine(string value, Color foreground, Color background)
        {
            SetColorForeground(foreground);
            SetColorBackground(background);
            WriteLine(value);
        }

        /// <summary>
        ///     Write the specified string value to the standard output.
        /// </summary>
        /// <param name="value">
        ///     The value to write.
        /// </param>
        [PublicAPI]
        public static void Write(string value)
        {
            Console.Write(value);
        }

        /// <summary>
        ///     Write the specified string value to the standard output.
        /// </summary>
        /// <param name="value">
        ///     The value to write.
        /// </param>
        /// <param name="foreground">
        ///     The color for the text.
        /// </param>
        [PublicAPI]
        public static void Write(string value, Color foreground)
        {
            SetColorForeground(foreground);
            Console.Write(value);
        }

        /// <summary>
        ///     Write the specified string value to the standard output.
        /// </summary>
        /// <param name="value">
        ///     The value to write.
        /// </param>
        /// <param name="foreground">
        ///     The color for the text.
        /// </param>
        /// <param name="background">
        ///     The color for the background.
        /// </param>
        [PublicAPI]
        public static void Write(string value, Color foreground, Color background)
        {
            SetColorForeground(foreground);
            SetColorBackground(background);
            Console.Write(value);
        }

        /// <summary>
        ///     Write the concatenation of specified objects to the standard output.
        /// </summary>
        /// <param name="objects">
        ///     An object array that contains the elements to concatenate.
        /// </param>
        [PublicAPI]
        public static void WriteConcat(params object[] objects)
        {
            Console.Write(string.Concat(objects));
        }

        #endregion
    }
}