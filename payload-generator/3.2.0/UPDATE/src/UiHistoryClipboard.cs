using System;
using Android.Runtime;

namespace Tayson.Providers;

/// <summary>
/// Clipboard bridge using the same JNI APIs as the original app's paste fix.
/// No additional trimmed MAUI types or persistently stored secrets.
/// </summary>
public static class UiHistoryClipboard
{
    public static void Copy(string backup)
    {
        IntPtr klass = IntPtr.Zero;
        try
        {
            var context = Android.App.Application.Context;
            using Java.Lang.Object service = context.GetSystemService("clipboard");
            if (service == null) throw new InvalidOperationException("Clipboard indisponível.");
            klass = JNIEnv.GetObjectClass(service.Handle);
            IntPtr method = JNIEnv.GetMethodID(klass, "setText", "(Ljava/lang/CharSequence;)V");
            if (method == IntPtr.Zero) throw new InvalidOperationException("Clipboard incompatível.");
            using var value = new Java.Lang.String(backup ?? "");
            JNIEnv.CallVoidMethod(service.Handle, method, new JValue(value.Handle));
        }
        finally
        {
            if (klass != IntPtr.Zero) JNIEnv.DeleteLocalRef(klass);
        }
    }

    public static string Paste()
    {
        IntPtr klass = IntPtr.Zero;
        IntPtr charClass = IntPtr.Zero;
        IntPtr chars = IntPtr.Zero;
        try
        {
            var context = Android.App.Application.Context;
            using Java.Lang.Object service = context.GetSystemService("clipboard");
            if (service == null) return "";
            klass = JNIEnv.GetObjectClass(service.Handle);
            IntPtr get = JNIEnv.GetMethodID(klass, "getText", "()Ljava/lang/CharSequence;");
            if (get == IntPtr.Zero) return "";
            chars = JNIEnv.CallObjectMethod(service.Handle, get);
            if (chars == IntPtr.Zero) return "";
            charClass = JNIEnv.GetObjectClass(chars);
            IntPtr str = JNIEnv.GetMethodID(charClass, "toString", "()Ljava/lang/String;");
            IntPtr s = JNIEnv.CallObjectMethod(chars, str);
            return s == IntPtr.Zero ? "" : JNIEnv.GetString(s, JniHandleOwnership.TransferLocalRef) ?? "";
        }
        finally
        {
            if (charClass != IntPtr.Zero) JNIEnv.DeleteLocalRef(charClass);
            if (chars != IntPtr.Zero) JNIEnv.DeleteLocalRef(chars);
            if (klass != IntPtr.Zero) JNIEnv.DeleteLocalRef(klass);
        }
    }
}
