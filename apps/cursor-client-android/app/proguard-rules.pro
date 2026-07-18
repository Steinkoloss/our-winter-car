-dontwarn com.google.errorprone.annotations.**
-keepattributes *Annotation*
-keepclassmembers class kotlinx.serialization.json.** { *; }
-keep,includedescriptorclasses class com.cursor.client.api.**$$serializer { *; }
-keepclassmembers class com.cursor.client.api.** {
    *** Companion;
}
-keepclasseswithmembers class com.cursor.client.api.** {
    kotlinx.serialization.KSerializer serializer(...);
}
