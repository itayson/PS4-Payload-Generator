using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Android.Runtime;
using Microsoft.Maui.Storage;

namespace Tayson.Providers;

/// <summary>
/// User-directed Android Storage Access Framework (SAF) export/import.
/// No direct MediaStore insert, no broad storage permission, no cloud calls.
/// The original trimmed MAUI runtime has its internal activity-result registry,
/// which we use for ACTION_CREATE_DOCUMENT without modifying MainActivity.
/// </summary>
public static class UiHistoryDocuments
{
    public static async Task<string> PickBackupTextAsync()
    {
        FileResult picked = await FilePicker.Default.PickAsync(
            new PickOptions { PickerTitle = "Selecionar backup Tayson (.txt)" });
        if (picked == null) return null;
        if (string.IsNullOrWhiteSpace(picked.FileName) ||
            !picked.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Selecione um arquivo .txt de backup.");
        using Stream stream = await picked.OpenReadAsync();
        return UiHistoryTextFile.Decode(stream);
    }

    public static async Task<string> ExportToDocumentAsync(string backup)
    {
        // Validate full content before opening the system picker or writing anything.
        byte[] bytes = UiHistoryTextFile.Encode(backup);
        string filename = "Tayson-Historico-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt";
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity == null) throw new InvalidOperationException("Activity Android indisponível.");

        Type registry = typeof(Microsoft.Maui.Platform.ActivityResultCallbackRegistry);
        var binding = BindingFlags.Static | BindingFlags.NonPublic;
        MethodInfo register = registry.GetMethod("RegisterActivityResultCallback", binding);
        MethodInfo unregister = registry.GetMethod("UnregisterActivityResultCallback", binding);
        if (register == null || unregister == null)
            throw new NotSupportedException("Seletor de documentos indisponível nesta versão.");

        var completion = new TaskCompletionSource<Android.Net.Uri>();
        var callback = new Action<Android.App.Result, Android.Content.Intent>((result, returned) =>
        {
            if (result == Android.App.Result.Ok && returned?.Data != null)
                completion.TrySetResult(returned.Data);
            else
                completion.TrySetResult(null);
        });
        int requestCode = (int)register.Invoke(null, new object[] { callback });
        try
        {
            using (var intent = new Android.Content.Intent("android.intent.action.CREATE_DOCUMENT"))
            {
                intent.AddCategory("android.intent.category.OPENABLE");
                intent.SetType("text/plain");
                intent.PutExtra("android.intent.extra.TITLE", filename);
                activity.StartActivityForResult(intent, requestCode);
            }
            Android.Net.Uri document = await completion.Task;
            if (document == null) return null; // User canceled.
            using (document)
            using (var descriptor = activity.ContentResolver.OpenFileDescriptor(document, "w"))
            {
                if (descriptor == null)
                    throw new IOException("Não foi possível abrir o arquivo selecionado.");
                WriteToDescriptor(descriptor, bytes);
            }
            return filename;
        }
        finally
        {
            unregister.Invoke(null, new object[] { requestCode });
        }
    }

    /// <summary>
    /// The inherited Xamarin.Android assembly has OpenFileDescriptor but no typed
    /// FileOutputStream(FileDescriptor) constructor. Construct the Java stream from
    /// the document descriptor, with its own scoped JNI references.
    /// </summary>
    static unsafe void WriteToDescriptor(Android.OS.ParcelFileDescriptor descriptor, byte[] content)
    {
        IntPtr descriptorClass = IntPtr.Zero;
        IntPtr fd = IntPtr.Zero;
        IntPtr outputClass = IntPtr.Zero;
        try
        {
            descriptorClass = JNIEnv.GetObjectClass(descriptor.Handle);
            IntPtr getter = JNIEnv.GetMethodID(descriptorClass, "getFileDescriptor",
                "()Ljava/io/FileDescriptor;");
            fd = JNIEnv.CallObjectMethod(descriptor.Handle, getter);
            if (fd == IntPtr.Zero) throw new IOException("Descritor de arquivo indisponível.");

            outputClass = JNIEnv.FindClass("java/io/FileOutputStream");
            IntPtr constructor = JNIEnv.GetMethodID(outputClass, "<init>",
                "(Ljava/io/FileDescriptor;)V");
            JValue* arguments = stackalloc JValue[1];
            arguments[0] = new JValue(fd);
            IntPtr handle = JNIEnv.NewObject(outputClass, constructor, arguments);
            if (handle == IntPtr.Zero) throw new IOException("Não foi possível iniciar a gravação.");
            using (var output = Java.Lang.Object.GetObject<Java.IO.OutputStream>(
                handle, JniHandleOwnership.TransferLocalRef))
            {
                output.Write(content, 0, content.Length);
                output.Flush();
            }
        }
        finally
        {
            if (outputClass != IntPtr.Zero) JNIEnv.DeleteLocalRef(outputClass);
            if (fd != IntPtr.Zero) JNIEnv.DeleteLocalRef(fd);
            if (descriptorClass != IntPtr.Zero) JNIEnv.DeleteLocalRef(descriptorClass);
        }
    }
}
