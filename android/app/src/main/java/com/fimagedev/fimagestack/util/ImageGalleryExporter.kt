package com.fimagedev.fimagestack.util

import android.content.ContentValues
import android.content.Context
import android.content.Intent
import android.graphics.Bitmap
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.provider.MediaStore
import android.widget.Toast
import androidx.core.content.FileProvider
import java.io.File
import java.io.FileOutputStream
import java.io.OutputStream

object ImageGalleryExporter {

    /**
     * Saves bitmap to the device Gallery / DCIM / Pictures album and makes it immediately visible
     */
    fun saveImageToGallery(
        context: Context,
        bitmap: Bitmap,
        title: String = "FStack_Macro_${System.currentTimeMillis()}"
    ): Uri? {
        val fileName = "$title.jpg"
        val contentResolver = context.contentResolver

        val contentValues = ContentValues().apply {
            put(MediaStore.Images.Media.DISPLAY_NAME, fileName)
            put(MediaStore.Images.Media.MIME_TYPE, "image/jpeg")
            put(MediaStore.Images.Media.DATE_ADDED, System.currentTimeMillis() / 1000)
            put(MediaStore.Images.Media.DATE_TAKEN, System.currentTimeMillis())

            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                put(MediaStore.Images.Media.RELATIVE_PATH, Environment.DIRECTORY_PICTURES + "/FImageStack")
                put(MediaStore.Images.Media.IS_PENDING, 1)
            }
        }

        val uri = contentResolver.insert(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, contentValues) ?: return null

        try {
            contentResolver.openOutputStream(uri)?.use { outStream: OutputStream ->
                bitmap.compress(Bitmap.CompressFormat.JPEG, 98, outStream)
            }

            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                contentValues.clear()
                contentValues.put(MediaStore.Images.Media.IS_PENDING, 0)
                contentResolver.update(uri, contentValues, null, null)
            }

            return uri
        } catch (e: Exception) {
            contentResolver.delete(uri, null, null)
            return null
        }
    }

    /**
     * Shares bitmap directly to other apps (Zalo, Telegram, Google Drive, Mail)
     */
    fun shareImage(context: Context, bitmap: Bitmap) {
        try {
            val cachePath = File(context.cacheDir, "shared_images").also { it.mkdirs() }
            val shareFile = File(cachePath, "fimagestack_macro_${System.currentTimeMillis()}.jpg")

            FileOutputStream(shareFile).use { out ->
                bitmap.compress(Bitmap.CompressFormat.JPEG, 98, out)
            }

            val contentUri: Uri = FileProvider.getUriForFile(
                context,
                "com.fimagedev.fimagestack.fileprovider",
                shareFile
            )

            val shareIntent = Intent(Intent.ACTION_SEND).apply {
                type = "image/jpeg"
                putExtra(Intent.EXTRA_STREAM, contentUri)
                putExtra(Intent.EXTRA_TEXT, "Exported from FImageStack Pro Macro")
                addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
            }

            context.startActivity(Intent.createChooser(shareIntent, "Share Macro Master Image"))
        } catch (e: Exception) {
            Toast.makeText(context, "Share error: ${e.message}", Toast.LENGTH_SHORT).show()
        }
    }
}
