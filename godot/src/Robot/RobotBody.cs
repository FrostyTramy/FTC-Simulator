using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using Sim.App;
using Sim.Core.Numerics;
using Sim.Field;
using Sim.Hardware;
using Sim.Robots;

namespace Sim.Robot;

/// <summary>Sets a robot's commands each physics tick (direct drive, or the OpMode runner).</summary>
public interface IRobotDriver
{
    /// <summary>Before the powertrain step: set this tick's commands.</summary>
    void BeforeStep(RobotBody robot, long tick);

    /// <summary>After the powertrain step: the runner applies the OpMode's new commands here (they act next tick).</summary>
    void AfterStep(RobotBody robot, long tick);
}

/// <summary>
/// A robot on the field (spec 05): one rigid chassis (mass, centre of mass and inertia from the robot YAML, colliders
/// from the CAD pipeline) carried by raycast wheel suspension (a stiff spring-damper per wheel, so the suspension forces
/// are the wheels' normal loads, weight transfer included) and driven by the shared <see cref="RobotPowertrain"/> (hub
/// firmware, battery bus, mecanum force model, mechanism shafts, servos). Meshes stay in pipeline coordinates under a
/// node with the YAML cad_to_robot transform (after undoing the pipeline's Y-up turn); wheels and mechanisms spin about
/// their axles.
/// Mechanisms (spec 04): rollers, wheels and flywheels with a radius touch game elements through
/// <see cref="CompliantContact"/> (no colliders; the reaction loads their motor or servo); paddle wheels are kinematic
/// bodies turned by their servo. Robot frame = Godot local frame: +X right, +Y up, −Z forward; origin on the floor.
/// </summary>
public partial class RobotBody : RigidBody3D
{
    private readonly List<(Node3D Pivot, Vector3 AxisCad, Func<double> Angle)> _spinners = new();
    private readonly List<RollerMechanism> _rollers = new();
    private readonly List<(AnimatableBody3D Body, Vector3 CenterCad, Vector3 AxisCad, Func<double> Angle)> _paddles = new();
    private Vector3[] _wheelCenters = Array.Empty<Vector3>();   // robot frame
    private double[] _normals = Array.Empty<double>();
    private Transform3D _meshToRobot;
    private PhysicsConfig _physics = null!;
    private uint _groundMask;
    private double _stiffness;
    private double _damping;
    private long _tick;
    private GuidedBallPath? _ballPath;
    private readonly Dictionary<RigidBody3D, int> _elementIds = new();
    private readonly Dictionary<int, (FieldElement Element, uint Layer, uint Mask)> _stored = new();
    private double _lastFeederAngle;

    /// <summary>A roller/wheel/flywheel that grips elements: axle in the robot frame, the port that drives it.</summary>
    private sealed record RollerMechanism(MechanismDef Def, Vector3 AxisPoint, Vector3 Axis, MotorPort? Motor, ServoPort? Servo)
    {
        /// <summary>Spin about <see cref="Axis"/>, rad/s (0 for a fixed bar).</summary>
        public double Omega => Motor != null
            ? Motor.OutputOmega * Motor.Device.SpeedRatio * Def.DriveSign
            : Servo != null ? Mathf.DegToRad(Servo.Model.SpeedDegPerS) * Def.DriveSign : 0;

        /// <summary>Hands a torque on the roller (about <see cref="Axis"/>) to its motor or servo.</summary>
        public void Load(double torque)
        {
            if (Motor != null)
            {
                Motor.LoadTorque += torque * Def.DriveSign * Motor.Device.SpeedRatio;
            }
            else if (Servo != null)
            {
                Servo.LoadTorque -= torque * Def.DriveSign;   // ServoModel: load opposing positive rotation
            }
        }
    }

    public RobotPowertrain Powertrain { get; private set; } = null!;
    public RobotDefinition Definition { get; private set; } = null!;
    public HardwareConfig HardwareConfig { get; private set; } = null!;
    public IRobotDriver? Driver { get; set; }

    /// <summary>The field has disabled this robot (match control): motors and servos get no power.</summary>
    public bool FieldDisabled { get; set; }

    /// <summary>The field's game elements, for intake and launcher contacts (null: no element interaction).</summary>
    public IReadOnlyList<FieldElement>? Elements { get; set; }

    /// <summary>Simulation time at the end of the last physics tick, ns.</summary>
    public long NowNanos => _tick * 1_000_000_000L / Engine.PhysicsTicksPerSecond;

    /// <summary>Convex outline of the robot on the floor in the robot frame (x, z), colliders and wheels included.</summary>
    public IReadOnlyList<Vector2> FootprintLocal { get; private set; } = Array.Empty<Vector2>();

    /// <summary>Highest collider point above the robot origin (floor), m.</summary>
    public float TopLocal { get; private set; }

    /// <summary>Normal force on each wheel in the last tick (robot YAML wheel order), N.</summary>
    public IReadOnlyList<double> WheelNormals => _normals;

    /// <summary>Velocity of the robot origin in the robot frame (X right, Z back) and yaw rate (CCW from above).</summary>
    public (Vec2d Velocity, double YawRate) PlanarMotion()
    {
        var local = GlobalBasis.Inverse() * VelocityAt(GlobalPosition);
        var up = GlobalBasis.Y.Normalized();
        return (new Vec2d(local.X, local.Z), AngularVelocity.Dot(up));
    }

    /// <summary>Velocity of a world point fixed to the chassis.</summary>
    public Vector3 VelocityAt(Vector3 point) => LinearVelocity + AngularVelocity.Cross(point - GlobalTransform * CenterOfMass);

    /// <summary>Loads robot NAME (config/robots/NAME.yaml + generated/NAME) and adds it to <paramref name="parent"/> at <paramref name="pose"/>.</summary>
    public static RobotBody Spawn(Node parent, string name, Transform3D pose, PhysicsConfig physics)
    {
        var definition = RobotDefinition.Load(RepoPaths.Config(Path.Combine("robots", name + ".yaml")));
        var hardware = HardwareConfig.Load(Path.Combine(RepoPaths.RepoRoot, "config"));
        var contact = DriveContact.Load(RepoPaths.Config("physics.yaml"));
        var robot = new RobotBody
        {
            Name = name,
            Definition = definition,
            HardwareConfig = hardware,
            Powertrain = new RobotPowertrain(definition, hardware, contact, Engine.PhysicsTicksPerSecond),
            Mass = (float)definition.MassKg,
            CenterOfMassMode = CenterOfMassModeEnum.Custom,
            CenterOfMass = ToGodot(definition.CenterOfMass),
            Inertia = ToGodot(definition.Inertia),
            CollisionLayer = CollisionLayers.Robots,
            CollisionMask = CollisionLayers.FieldStatic | CollisionLayers.FieldMoving | CollisionLayers.Elements | CollisionLayers.Robots,
            PhysicsMaterialOverride = physics.RobotChassis(),
            CanSleep = false,
            Transform = pose,
        };
        var (stiffness, damping) = physics.WheelSuspension(definition.MassKg / definition.Wheels.Count);
        robot._physics = physics;
        robot._stiffness = stiffness;
        robot._damping = damping;
        robot._groundMask = CollisionLayers.FieldStatic | CollisionLayers.FieldMoving | CollisionLayers.Elements;
        robot._wheelCenters = definition.Wheels.Select(w => ToGodot(w.Position)).ToArray();
        robot._normals = new double[definition.Wheels.Count];
        parent.AddChild(robot);
        robot.Build(name, parent);
        robot.MeasureOutline();
        robot._ballPath = definition.BallPath is { } path ? new GuidedBallPath(path) : null;
        return robot;
    }

    /// <summary>Elements held in the robot by the guided path (hopper), oldest first.</summary>
    public IEnumerable<FieldElement> StoredElements => _ballPath?.Stored.Select(id => _stored[id].Element) ?? Enumerable.Empty<FieldElement>();

    /// <summary>Puts an element straight into the hopper (pre-loads at match setup, G304.G). False if full or no hopper.</summary>
    public bool Preload(FieldElement element)
    {
        if (_ballPath == null || !_ballPath.Preload(IdOf(element.Body)))
        {
            return false;
        }
        Store(element);
        return true;
    }

    /// <summary>Footprint (convex hull in x/z) and top height from the chassis colliders plus the wheel cylinders.</summary>
    private void MeasureOutline()
    {
        var points = new List<Vector3>();
        foreach (var shape in GetChildren().OfType<CollisionShape3D>())
        {
            var t = shape.Transform;
            switch (shape.Shape)
            {
                case ConvexPolygonShape3D convex:
                    points.AddRange(convex.Points.Select(v => t * v));
                    break;
                case BoxShape3D box:
                    var h = box.Size / 2;
                    for (var i = 0; i < 8; i++)
                    {
                        points.Add(t * new Vector3((i & 1) == 0 ? -h.X : h.X, (i & 2) == 0 ? -h.Y : h.Y, (i & 4) == 0 ? -h.Z : h.Z));
                    }
                    break;
            }
        }
        var halfWidth = (float)Definition.Wheels.Count > 0 ? 0.02f : 0f;
        foreach (var c in _wheelCenters)
        {
            var r = c.Y;   // wheel radius = centre height above the floor
            foreach (var dx in new[] { -halfWidth, halfWidth })
            {
                foreach (var dz in new[] { -r, r })
                {
                    points.Add(new Vector3(c.X + dx, c.Y, c.Z + dz));
                }
            }
        }
        TopLocal = points.Count == 0 ? 0 : points.Max(p => p.Y);
        FootprintLocal = ConvexHull(points.Select(p => new Vector2(p.X, p.Z)).ToList());
    }

    /// <summary>Andrew's monotone chain, counter-clockwise, no collinear points.</summary>
    private static List<Vector2> ConvexHull(List<Vector2> pts)
    {
        pts = pts.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        if (pts.Count < 3)
        {
            return pts;
        }
        static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        var hull = new List<Vector2>();
        foreach (var pass in new[] { pts, Enumerable.Reverse(pts).ToList() })
        {
            var start = hull.Count;
            foreach (var p in pass)
            {
                while (hull.Count >= start + 2 && Cross(hull[^2], hull[^1], p) <= 0)
                {
                    hull.RemoveAt(hull.Count - 1);
                }
                hull.Add(p);
            }
            hull.RemoveAt(hull.Count - 1);
        }
        return hull;
    }

    private void Build(string name, Node parent)
    {
        var dir = $"res://generated/{name}/";
        var layout = JsonSerializer.Deserialize<FieldLayout>(Godot.FileAccess.GetFileAsString(dir + "layout.json"), FieldLayout.Json)
            ?? throw new InvalidDataException($"{dir}layout.json is empty");
        _meshToRobot = CadToRobot(Definition.CadToRobot) * MeshToCad(layout.UpAxis);
        var cadRoot = new Node3D { Name = "Cad", Transform = _meshToRobot };
        AddChild(cadRoot);
        var toCad = _meshToRobot.AffineInverse();
        foreach (var group in layout.Groups)
        {
            var mesh = group.Mesh != null ? MeshLoader.Instantiate(dir + group.Mesh) : null;
            var spin = SpinFor(group.Name, toCad);
            if (mesh != null)
            {
                if (spin is { } s)
                {
                    var pivot = new Node3D { Name = group.Name, Position = s.CenterCad };
                    mesh.Position = -s.CenterCad;
                    pivot.AddChild(mesh);
                    cadRoot.AddChild(pivot);
                    _spinners.Add((pivot, s.AxisCad, s.Angle));
                }
                else
                {
                    mesh.Name = group.Name;
                    cadRoot.AddChild(mesh);
                }
            }
            if (group.Colliders == null || group.Name.StartsWith("wheel_", StringComparison.Ordinal))
            {
                continue;   // the wheels touch the ground through the suspension rays, not through colliders
            }
            var mechanism = MechanismFor(group.Name);
            if (mechanism is { Radius: not null, AxisPoint: { } point, Axis: { } axis })
            {
                var (motor, servo) = DriverOf(mechanism);
                _rollers.Add(new RollerMechanism(mechanism, ToGodot(point), ToGodot(axis).Normalized(), motor, servo));
                continue;   // gripping surfaces act through CompliantContact, not through colliders
            }
            var specs = JsonSerializer.Deserialize<List<ColliderSpec>>(Godot.FileAccess.GetFileAsString(dir + group.Colliders), FieldLayout.Json)
                ?? new List<ColliderSpec>();
            if (mechanism?.Type == "paddle_wheel" && spin is { } paddle)
            {
                // Paddles push elements kinematically; they never touch their own chassis (separate layer).
                var body = new AnimatableBody3D
                {
                    Name = name + "_" + group.Name,
                    SyncToPhysics = true,
                    CollisionLayer = CollisionLayers.RobotMechanisms,
                    CollisionMask = CollisionLayers.Elements,
                };
                foreach (var spec in specs)
                {
                    body.AddChild(ShapeFactory.Create(spec));   // CAD coordinates
                }
                parent.AddChild(body);
                _paddles.Add((body, paddle.CenterCad, paddle.AxisCad, paddle.Angle));
                body.GlobalTransform = PaddleTransform(GlobalTransform, paddle.CenterCad, paddle.AxisCad, paddle.Angle());
                continue;
            }
            foreach (var spec in specs)
            {
                var shape = ShapeFactory.Create(spec);
                shape.Transform = _meshToRobot * shape.Transform;   // CollisionShape3D must be a direct child of the body
                AddChild(shape);
            }
        }
    }

    private MechanismDef? MechanismFor(string group) =>
        Definition.Mechanisms.FirstOrDefault(m => m.Id == group || m.Id.EndsWith("_" + group, StringComparison.Ordinal));

    private (MotorPort? Motor, ServoPort? Servo) DriverOf(MechanismDef mechanism)
    {
        var drives = "mechanism:" + mechanism.Id;
        return (Powertrain.Hardware.Motors.FirstOrDefault(m => m.Device.Drives == drives),
                Powertrain.Hardware.Servos.FirstOrDefault(s => s.Device.Drives == drives));
    }

    /// <summary>Axle (CAD frame) and angle source for a wheel or mechanism group; null for parts that do not spin.</summary>
    private (Vector3 CenterCad, Vector3 AxisCad, Func<double> Angle)? SpinFor(string group, Transform3D toCad)
    {
        var drive = Powertrain.Drive;
        for (var i = 0; i < Definition.Wheels.Count; i++)
        {
            if (group == "wheel_" + Definition.Wheels[i].Id)
            {
                var index = i;
                // Rolling forward (−Z) turns the wheel about the robot's −X axis.
                return (toCad * _wheelCenters[i], toCad.Basis * new Vector3(-1, 0, 0), () => drive.WheelAngle(index));
            }
        }
        if (MechanismFor(group) is not { AxisPoint: { } point, Axis: { } axis } mechanism)
        {
            return null;
        }
        var center = toCad * ToGodot(point);
        var axisCad = toCad.Basis * ToGodot(axis).Normalized();
        var (motor, servo) = DriverOf(mechanism);
        var sign = mechanism.DriveSign;
        if (motor != null)
        {
            var ratio = motor.Device.SpeedRatio;
            return (center, axisCad, () => motor.OutputAngle * ratio * sign);
        }
        return servo != null ? (center, axisCad, () => Mathf.DegToRad(servo.Model.AngleDeg) * sign) : null;
    }

    public override void _PhysicsProcess(double delta)
    {
        Driver?.BeforeStep(this, _tick);
        if (FieldDisabled)
        {
            foreach (var m in Powertrain.Hardware.Motors)
            {
                m.Controller.SetPower(0);
                m.Controller.Enabled = false;
            }
            foreach (var s in Powertrain.Hardware.Servos)
            {
                s.Model.Enabled = false;
            }
        }
        ApplySuspension();
        ApplyElementContacts();
        RunBallPath();
        var (velocity, yawRate) = PlanarMotion();
        var forces = Powertrain.Step(velocity, yawRate, _normals);
        var basis = GlobalBasis;
        foreach (var f in forces)
        {
            var force = basis * new Vector3((float)f.Force.X, 0, (float)f.Force.Z);
            ApplyForce(force, basis * new Vector3((float)f.Point.X, 0, (float)f.Point.Z));
        }
        MovePaddles(delta);
        Driver?.AfterStep(this, _tick);
        _tick++;
    }

    public override void _Process(double delta)
    {
        foreach (var (pivot, axis, angle) in _spinners)
        {
            pivot.Basis = new Basis(axis.Normalized(), (float)angle());
        }
    }

    /// <summary>
    /// One ray per wheel from its centre down the robot's −Y axis. Penetration p = (centre height) − (hit distance);
    /// N = k·p + c·ṗ (≥ 0) along the hit normal at the contact point. Wheels in the air carry nothing.
    /// </summary>
    private void ApplySuspension()
    {
        var space = GetWorld3D().DirectSpaceState;
        var basis = GlobalBasis;
        var down = -basis.Y.Normalized();
        var exclude = new Godot.Collections.Array<Rid> { GetRid() };
        for (var i = 0; i < _wheelCenters.Length; i++)
        {
            var rest = _wheelCenters[i].Y;   // wheel centre height above the floor at zero load
            var from = GlobalTransform * _wheelCenters[i];
            var query = PhysicsRayQueryParameters3D.Create(from, from + down * (rest + 0.03f), _groundMask, exclude);
            var hit = space.IntersectRay(query);
            if (hit.Count == 0)
            {
                _normals[i] = 0;
                continue;
            }
            var point = (Vector3)hit["position"];
            var normal = ((Vector3)hit["normal"]).Normalized();
            var penetration = rest - from.DistanceTo(point);
            if (penetration <= 0)
            {
                _normals[i] = 0;
                continue;
            }
            var approach = -VelocityAt(point).Dot(normal);
            var n = Math.Max(0, _stiffness * penetration + _damping * approach);
            _normals[i] = n;
            ApplyForce(normal * (float)n, point - GlobalPosition);
        }
    }

    /// <summary>Rollers, sweepers and flywheels squeezing nearby elements (forces on both bodies, torque on the roller).</summary>
    private void ApplyElementContacts()
    {
        if (Elements == null || _rollers.Count == 0)
        {
            return;
        }
        var transform = GlobalTransform;
        var reach = 0.6f;   // robot half-diagonal plus an element: anything farther cannot touch a mechanism
        foreach (var element in Elements)
        {
            var body = element.Body;
            if (!IsInstanceValid(body) || body.GlobalPosition.DistanceTo(GlobalPosition) > reach)
            {
                continue;
            }
            var props = _physics.Element(element.Group);
            var (kElement, c, material, slip) = _physics.ElementContact(element.Type);
            var ball = new Ball(V(body.GlobalPosition), props.RadiusM, V(body.LinearVelocity), V(body.AngularVelocity));
            foreach (var roller in _rollers)
            {
                var axisPoint = transform * roller.AxisPoint;
                var def = roller.Def;
                var r = new Roller(V(axisPoint), V(transform.Basis * roller.Axis), def.Radius!.Value, (def.Width ?? 0) / 2, roller.Omega,
                    V(VelocityAt(axisPoint)), V(AngularVelocity));
                var friction = _physics.PairFriction(def.ContactMaterial ?? "default", material);
                // Element and surface compliance in series (soft Gecko tread, foam bar rollers).
                var k = def.ContactStiffness is { } kSurface ? 1 / (1 / kElement + 1 / kSurface) : kElement;
                if (CompliantContact.Compute(r, ball, new ContactParams(k, c, friction, slip)) is not { } contact)
                {
                    continue;
                }
                var force = G(contact.Force);
                var point = G(contact.Point);
                body.ApplyForce(force, point - body.GlobalPosition);
                ApplyForce(-force, point - GlobalPosition);
                roller.Load(contact.ShaftTorque);
            }
        }
    }

    /// <summary>
    /// Guided element path (D-027): capture in front of the running intake roller, hold in the hopper slots, launch one
    /// element per feeder quarter turn at the flywheel's exit speed; the flywheel loses the angular momentum it gives.
    /// </summary>
    private void RunBallPath()
    {
        if (_ballPath is not { } path || Elements == null)
        {
            return;
        }
        var intake = _rollers.FirstOrDefault(r => r.Motor != null && r.Def.Id == "intake_roller");
        var flywheel = _rollers.FirstOrDefault(r => r.Motor != null && r.Def.Type == "flywheel");
        var feeder = Powertrain.Hardware.Servos.FirstOrDefault(sv => sv.Device.Drives == "mechanism:windmill");
        var transform = GlobalTransform;
        var inverse = transform.AffineInverse();

        // Capture: positive raw intake power pulls in (CODE:201-212), so the inward rim speed follows the motor.
        if (intake?.Motor is { } intakeMotor)
        {
            var inward = intakeMotor.OutputOmega * intakeMotor.Device.SpeedRatio * intake.Def.Radius!.Value;
            foreach (var element in Elements)
            {
                var body = element.Body;
                if (!IsInstanceValid(body) || body.Freeze || body.GlobalPosition.DistanceTo(GlobalPosition) > 0.5f)
                {
                    continue;
                }
                var local = inverse * body.GlobalPosition;
                if (path.TryCapture(IdOf(body), new System.Numerics.Vector3(local.X, local.Y, local.Z), inward))
                {
                    Store(element);
                }
            }
        }

        // Feed: the feeder's rotation in the direction positive SDK power turns it (CODE:291 setPower(1) feeds).
        if (feeder != null && flywheel?.Motor is { } launcher)
        {
            var rawFeedSign = path.Def.FeedPowerSign * (feeder.Device.Direction == "REVERSE" ? -1 : 1);
            var angle = feeder.Model.AngleDeg;
            var turned = (angle - _lastFeederAngle) * rawFeedSign;
            _lastFeederAngle = angle;
            var rim = Math.Abs(launcher.OutputOmega * launcher.Device.SpeedRatio) * flywheel.Def.Radius!.Value;
            if (path.Feed(turned, rim) is { } id)
            {
                Launch(id, path, path.ExitSpeed(rim), launcher, flywheel.Def.Radius!.Value);
            }
        }

        // Stored elements ride in their slots.
        foreach (var id in path.Stored)
        {
            var slot = path.SlotOf(id);
            _stored[id].Element.Body.GlobalTransform = new Transform3D(transform.Basis, transform * new Vector3(slot.X, slot.Y, slot.Z));
        }
    }

    private int IdOf(RigidBody3D body)
    {
        if (!_elementIds.TryGetValue(body, out var id))
        {
            id = _elementIds.Count + 1;
            _elementIds[body] = id;
        }
        return id;
    }

    /// <summary>Holds an element in the hopper: kinematic and without collisions until it is launched.</summary>
    private void Store(FieldElement element)
    {
        var body = element.Body;
        _stored[IdOf(body)] = (element, body.CollisionLayer, body.CollisionMask);
        body.FreezeMode = FreezeModeEnum.Kinematic;
        body.Freeze = true;
        body.CollisionLayer = 0;
        body.CollisionMask = 0;
    }

    private void Launch(int id, GuidedBallPath path, double exitSpeed, MotorPort launcher, double rimRadius)
    {
        var (element, layer, mask) = _stored[id];
        _stored.Remove(id);
        var body = element.Body;
        var t = GlobalTransform;
        var point = t * new Vector3(path.Def.LaunchPoint.X, path.Def.LaunchPoint.Y, path.Def.LaunchPoint.Z);
        var direction = t.Basis * new Vector3(path.Def.LaunchDirection.X, path.Def.LaunchDirection.Y, path.Def.LaunchDirection.Z);
        body.GlobalTransform = new Transform3D(Basis.Identity, point);
        body.Freeze = false;
        body.CollisionLayer = layer;
        body.CollisionMask = mask;
        body.LinearVelocity = VelocityAt(point) + direction.Normalized() * (float)exitSpeed;
        body.AngularVelocity = Vector3.Zero;
        // The flywheel gives the element m·v·R of angular momentum: its shaft slows down (RPM drop per shot).
        var impulse = GuidedBallPath.FlywheelImpulse(_physics.Element(element.Group).MassKg, exitSpeed, rimRadius);
        if (launcher.LoadInertia is { } inertia && launcher.OutputOmega != 0)
        {
            var drop = impulse * launcher.Device.SpeedRatio / inertia;
            launcher.OutputOmega -= Math.Sign(launcher.OutputOmega) * Math.Min(Math.Abs(launcher.OutputOmega), drop);
        }
    }

    /// <summary>Kinematic paddles go where the chassis will be at the end of this step, turned by their servo.</summary>
    private void MovePaddles(double dt)
    {
        if (_paddles.Count == 0)
        {
            return;
        }
        var t = GlobalTransform;
        var spin = AngularVelocity * (float)dt;
        var predicted = new Transform3D(
            spin.LengthSquared() > 1e-12f ? new Basis(spin.Normalized(), spin.Length()) * t.Basis : t.Basis,
            t.Origin + VelocityAt(t.Origin) * (float)dt);
        foreach (var (body, center, axis, angle) in _paddles)
        {
            body.GlobalTransform = PaddleTransform(predicted, center, axis, angle());
        }
    }

    private Transform3D PaddleTransform(Transform3D robot, Vector3 centerCad, Vector3 axisCad, double angle) =>
        robot * _meshToRobot * new Transform3D(Basis.Identity, centerCad) * new Transform3D(new Basis(axisCad.Normalized(), (float)angle), Vector3.Zero)
        * new Transform3D(Basis.Identity, -centerCad);

    private static Vec3 V(Vector3 v) => new(v.X, v.Y, v.Z);

    private static Vector3 G(Vec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);

    private static Vector3 ToGodot(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>
    /// The CAD pipeline writes meshes and colliders Y-up (tools/cad_pipeline/run.py UP_TRANSFORMS: a Z-up CAD point
    /// (x, y, z) becomes (x, z, −y)); this undoes it, so the robot YAML's cad_to_robot (on the original CAD axes) applies.
    /// </summary>
    private static Transform3D MeshToCad(string upAxis) => upAxis switch
    {
        "Y" => Transform3D.Identity,
        "Z" => new Transform3D(new Basis(new Vector3(1, 0, 0), new Vector3(0, 0, 1), new Vector3(0, -1, 0)), Vector3.Zero),
        _ => throw new InvalidDataException($"unsupported up axis '{upAxis}' in the robot layout"),
    };

    private static Transform3D CadToRobot(double[] m)
    {
        // Row-major robot = M · cad: the basis columns are the images of the CAD axes.
        var basis = new Basis(
            new Vector3((float)m[0], (float)m[4], (float)m[8]),
            new Vector3((float)m[1], (float)m[5], (float)m[9]),
            new Vector3((float)m[2], (float)m[6], (float)m[10]));
        return new Transform3D(basis, new Vector3((float)m[3], (float)m[7], (float)m[11]));
    }
}
