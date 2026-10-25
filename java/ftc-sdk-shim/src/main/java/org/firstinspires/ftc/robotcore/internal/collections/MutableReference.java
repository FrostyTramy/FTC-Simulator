package org.firstinspires.ftc.robotcore.internal.collections;

/** A settable holder (SDK 12.0 API). */
public class MutableReference<T> {
  protected T value;

  public MutableReference(T value) {
    this.value = value;
  }

  public MutableReference() {
    this(null);
  }

  public void setValue(T value) {
    this.value = value;
  }

  public T getValue() {
    return value;
  }

  @Override
  public String toString() {
    return String.valueOf(value);
  }
}
