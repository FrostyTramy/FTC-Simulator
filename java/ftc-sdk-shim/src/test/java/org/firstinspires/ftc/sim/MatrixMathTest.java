package org.firstinspires.ftc.sim;

import static org.junit.jupiter.api.Assertions.assertEquals;

import java.util.Random;
import org.firstinspires.ftc.robotcore.external.matrices.MatrixF;
import org.firstinspires.ftc.robotcore.external.matrices.OpenGLMatrix;
import org.firstinspires.ftc.robotcore.external.matrices.VectorF;
import org.firstinspires.ftc.robotcore.external.navigation.AngleUnit;
import org.firstinspires.ftc.robotcore.external.navigation.AxesOrder;
import org.firstinspires.ftc.robotcore.external.navigation.AxesReference;
import org.firstinspires.ftc.robotcore.external.navigation.Orientation;
import org.firstinspires.ftc.robotcore.external.navigation.Quaternion;
import org.junit.jupiter.api.Test;

/** SDK matrix and Euler-angle conventions the vision code relies on. */
class MatrixMathTest {
  private static void assertSameRotation(MatrixF a, MatrixF b) {
    for (int i = 0; i < 3; i++) {
      for (int j = 0; j < 3; j++) {
        assertEquals(a.get(i, j), b.get(i, j), 1e-4, "element " + i + "," + j);
      }
    }
  }

  @Test
  void every_convention_round_trips_through_its_rotation_matrix() {
    Random random = new Random(7);
    for (AxesReference reference : AxesReference.values()) {
      for (AxesOrder order : AxesOrder.values()) {
        for (int n = 0; n < 20; n++) {
          float a = (float) (random.nextDouble() * 340 - 170);
          float b = (float) (random.nextDouble() * 160 - 80) + (order.indices()[0] == order.indices()[2] ? 90 : 0);
          float c = (float) (random.nextDouble() * 340 - 170);
          OpenGLMatrix m = Orientation.getRotationMatrix(reference, order, AngleUnit.DEGREES, a, b, c);
          Orientation o = Orientation.getOrientation(m, reference, order, AngleUnit.DEGREES);
          assertSameRotation(m, o.getRotationMatrix());
          Orientation other = Orientation.getOrientation(m, reference, order, AngleUnit.DEGREES, Orientation.AngleSet.THEOTHER);
          assertSameRotation(m, other.getRotationMatrix());
          assertSameRotation(m, Quaternion.fromOrientation(o).toMatrix());
        }
      }
    }
  }

  @Test
  void camera_pitch_minus_90_looks_forward_with_image_y_down() {
    // AprilTagProcessor.Builder: intrinsic ZXZ (yaw, pitch, roll); the SDK sample's (0, -90, 0) is a level camera.
    OpenGLMatrix r = new Orientation(AxesReference.INTRINSIC, AxesOrder.ZXZ, AngleUnit.DEGREES, 0, -90, 0, 0).getRotationMatrix();
    VectorF cameraZ = r.getColumn(2); // optical axis in robot coordinates
    VectorF cameraY = r.getColumn(1); // image down
    assertEquals(1, cameraZ.get(1), 1e-6);
    assertEquals(-1, cameraY.get(2), 1e-6);
  }

  @Test
  void opengl_transforms_post_multiply_and_invert() {
    OpenGLMatrix m = OpenGLMatrix.identityMatrix().translated(1, 2, 3).rotated(AngleUnit.DEGREES, 90, 0, 0, 1);
    VectorF p = m.transform(new VectorF(1, 0, 0)); // rotate first, then translate
    assertEquals(1, p.get(0), 1e-6);
    assertEquals(3, p.get(1), 1e-6);
    assertEquals(3, p.get(2), 1e-6);
    VectorF t = m.getTranslation();
    assertEquals(2, t.get(1), 1e-6);
    MatrixF identity = m.multiplied(m.inverted());
    for (int i = 0; i < 4; i++) {
      for (int j = 0; j < 4; j++) {
        assertEquals(i == j ? 1 : 0, identity.get(i, j), 1e-5);
      }
    }
    Quaternion q = Quaternion.fromMatrix(m, 0);
    assertSameRotation(m, q.toMatrix());
  }
}
