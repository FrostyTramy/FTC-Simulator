package org.firstinspires.ftc.robotcore.external.stream;

import android.graphics.Bitmap;
import org.firstinspires.ftc.robotcore.external.function.Consumer;
import org.firstinspires.ftc.robotcore.external.function.Continuation;

/** Something that can give the Driver Station a camera frame (SDK 12.0 public API). */
public interface CameraStreamSource {
  void getFrameBitmap(Continuation<? extends Consumer<Bitmap>> continuation);
}
