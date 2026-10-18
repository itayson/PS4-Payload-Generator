using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Android.Runtime;
using Microsoft.Maui.Storage;

namespace Tayson.Providers;

/// <summary>
/// Android SAF import + MediaStore Downloads export. Neither step sends data to a server.
/// Exported text is NOT encrypted and survives removing this application.
/// </summary>
public static class UiHistoryDocuments
{
    public static async Task<string> PickBackupTextAsync()
    {
        FileResult picked = await FilePicker.Default.PickAsync(
            new PickOptions { PickerTitle = "Selecionar backup Tayson (.txt)" });
        if (picked == null) return null; // User canceled.
        if (string.IsNullOrWhiteSpace(picked.FileName) ||
            !picked.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Selecione um arquivo .txt de backup.");
        using Stream stream = await picked.OpenReadAsync();
        return UiHistoryTextFile.Decode(stream);
    }

    /// <summary>
    /// Write directly to the public Android Downloads collection (Android 10+)
    /// with ContentResolver/MediaStore. Original trimmed MAUI DLLs have no typed
    /// ContentValues, Insert or OpenOutputStream bindings, so invoke Android API via JNI.
    /// </summary>
    public static unsafe string ExportToDownloads(string backup)
    {
        byte[] data = UiHistoryTextFile.Encode(backup);
        string filename = "Tayson-Historico-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt";
        var context = Android.App.Application.Context;
        var resolver = context?.ContentResolver;
        if (resolver == null) throw new InvalidOperationException("Armazenamento Android indisponível.");

        IntPtr cls = IntPtr.Zero;
        IntPtr resolverCls = IntPtr.Zero;
        IntPtr valuesHandle = IntPtr.Zero;
        Android.Net.Uri created = null;
        try
        {
            using var downloads = Android.Net.Uri.Parse("content://media/external_primary/downloads");
            cls = JNIEnv.FindClass("android/content/ContentValues");
            if (cls == IntPtr.Zero) throw new InvalidOperationException("MediaStore indisponível.");
            var constructor = JNIEnv.GetMethodID(cls, "<init>", "()V");
            valuesHandle = JNIEnv.NewObject(cls, constructor, null);
            if (valuesHandle == IntPtr.Zero) throw new InvalidOperationException("MediaStore não inicializado.");
            var put = JNIEnv.GetMethodID(cls, "put", "(Ljava/lang/String;Ljava/lang/String;)V");
            PutString(valuesHandle, put, "_display_name", filename);
            PutString(valuesHandle, put, "mime_type", "text/plain");
            PutString(valuesHandle, put, "relative_path", "Download/Tayson-PS4-Backups/");

            resolverCls = JNIEnv.GetObjectClass(resolver.Handle);
            var insert = JNIEnv.GetMethodID(resolverCls, "insert",
                "(Landroid/net/Uri;Landroid/content/ContentValues;)Landroid/net/Uri;");
            IntPtr added = JNIEnv.CallObjectMethod(resolver.Handle, insert,
                new JValue[] { new JValue(downloads.Handle), new JValue(valuesHandle) });
            if (added == IntPtr.Zero) throw new IOException("Falha ao criar arquivo em Downloads.");
            created = Java.Lang.Object.GetObject<Android.Net.Uri>(added, JniHandleOwnership.TransferLocalRef);

            var open = JNIEnv.GetMethodID(resolverCls, "openOutputStream",
                "(Landroid/net/Uri;)Ljava/io/OutputStream;");
            IntPtr javaStream = JNIEnv.CallObjectMethod(resolver.Handle, open,
                new JValue[] { new JValue(created.Handle) });
            if (javaStream == IntPtr.Zero) throw new IOException("Não foi possível gravar em Downloads.");
            using (var output = Java.Lang.Object.GetObject<Java.IO.OutputStream>(
                javaStream, JniHandleOwnership.TransferLocalRef))
            {
                output.Write(data, 0, data.Length);
                output.Flush();
            }
            return filename;
        }
        catch
        {
            // Clean up a partial file instead of leaving a corrupted-looking backup.
            if (created != null && resolverCls != IntPtr.Zero)
            {
                try
                {
                    var delete = JNIEnv.GetMethodID(resolverCls, "delete",
                        "(Landroid/net/Uri;Ljava/lang/String;[Ljava/lang/String;)I");
                    JValue[] args = new JValue[]
                    {
                        new JValue(created.Handle),
                        new JValue(IntPtr.Zero),
                        new JValue(IntPtr.Zero)
                    };
                    fixed (JValue* ptr = args)
                        JNIEnv.CallIntMethod(resolver.Handle, delete, ptr);
                }
                catch { }
            }
            throw;
        }
        finally
        {
            created?.Dispose();
            if (valuesHandle != IntPtr.Zero) JNIEnv.DeleteLocalRef(valuesHandle);
            if (resolverCls != IntPtr.Zero) JNIEnv.DeleteLocalRef(resolverCls);
            if (cls != IntPtr.Zero) JNIEnv.DeleteLocalRef(cls);
        }
    }

    static void PutString(IntPtr values, IntPtr put, string key, string value)
    {
        using var k = new Java.Lang.String(key);
        using var v = new Java.Lang.String(value);
        JNIEnv.CallVoidMethod(values, put,
            new JValue[] { new JValue(k.Handle), new JValue(v.Handle) });
    }
}
