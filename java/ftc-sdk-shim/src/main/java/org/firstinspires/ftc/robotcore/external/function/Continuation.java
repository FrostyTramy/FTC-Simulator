package org.firstinspires.ftc.robotcore.external.function;

/** A callback target the SDK dispatches on a thread or handler (SDK 12.0 API, subset: runs inline). */
public class Continuation<T> {
  private final T target;

  protected Continuation(T target) {
    this.target = target;
  }

  public static <T> Continuation<T> createTrivial(T target) {
    return new Continuation<>(target);
  }

  public T getTarget() {
    return target;
  }

  /** Runs the dispatcher with the target right away (no Android handler in the simulator). */
  public void dispatch(ContinuationResult<? super T> dispatcher) {
    dispatcher.handle(target);
  }

  /** What to do with the target. */
  public interface ContinuationResult<T> {
    void handle(T target);
  }
}
