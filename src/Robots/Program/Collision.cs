using Rhino.Geometry;

namespace Robots;

#if NETSTANDARD2_0
public class Collision
{
    public bool HasCollision => throw NotImplemented();
    public Mesh[] Meshes => throw NotImplemented();
    public CellTarget CollisionTarget => throw NotImplemented();

#pragma warning disable IDE0060
    internal Collision(Program program, IEnumerable<int> first, IEnumerable<int> second, Mesh? environment, int environmentPlane, double linearStep, double angularStep)
    {
        throw NotImplemented();
    }

    Exception NotImplemented() => new NotImplementedException(" Collisions not implemented in standalone.");
}

#elif NET48
using static System.Math;

public class Collision
{
    readonly Program _program;
    readonly RobotSystem _robotSystem;
    readonly double _linearStep;
    readonly double _angularStep;
    readonly IEnumerable<int> _first;
    readonly IEnumerable<int> _second;
    readonly Mesh? _environment;
    readonly int _environmentPlane;

    public Mesh[]? Meshes { get; private set; }
    public CellTarget? CollisionTarget { get; private set; }
    public bool HasCollision => CollisionTarget is not null;

    internal Collision(Program program, IEnumerable<int> first, IEnumerable<int> second, Mesh? environment, int environmentPlane, double linearStep, double angularStep)
    {

        _program = program;
        _robotSystem = program.RobotSystem;
        _linearStep = linearStep;
        _angularStep = angularStep;
        _first = first;
        _second = second;
        _environment = environment;
        _environmentPlane = environmentPlane;

        Collide();
    }

    void Collide()
    {
        Parallel.ForEach(_program.Targets, (cellTarget, state) =>
        {
            if (cellTarget.Index == 0)
                return;

            var prevcellTarget = _program.Targets[cellTarget.Index - 1];

            double divisions = 1;

            int groupCount = cellTarget.ProgramTargets.Count;

            for (int group = 0; group < groupCount; group++)
            {
                var target = cellTarget.ProgramTargets[group];
                var prevTarget = prevcellTarget.ProgramTargets[group];

                double distance = prevTarget.WorldPlane.Origin.DistanceTo(target.WorldPlane.Origin);
                double linearDivisions = Ceiling(distance / _linearStep);

                double maxAngle = target.Kinematics.Joints.Zip(prevTarget.Kinematics.Joints, (x, y) => Abs(x - y)).Max();
                double angularDivisions = Ceiling(maxAngle / _angularStep);

                double tempDivisions = Max(linearDivisions, angularDivisions);
                if (tempDivisions > divisions) divisions = tempDivisions;
            }

            var meshPoser = new RhinoMeshPoser(_program.RobotSystem);

            int j = (cellTarget.Index == 1) ? 0 : 1;

            for (int i = j; i < divisions; i++)
            {
                double t = (double)i / (double)divisions;
                var kineTargets = cellTarget.Lerp(prevcellTarget, _robotSystem, t, 0.0, 1.0);
                var kinematics = _program.RobotSystem.Kinematics(kineTargets);

                meshPoser.Pose(kinematics, cellTarget);
                var meshes = meshPoser.Meshes.NotNull();

                if (_environment is not null)
                {
                    if (_environmentPlane != -1)
                    {
                        Mesh currentEnvironment = _environment.DuplicateMesh();
                        var plane = kinematics.SelectMany(x => x.Planes).ToList()[_environmentPlane];
                        currentEnvironment.Transform(plane.ToTransform());
                        meshes.Add(currentEnvironment);
                    }
                    else
                    {
                        meshes.Add(_environment);
                    }
                }

                var setA = _first.Select(x => meshes[x]);
                var setB = _second.Select(x => meshes[x]);

                var meshClash = Rhino.Geometry.Intersect.MeshClash.Search(setA, setB, 1, 1);

                if (meshClash.Length > 0 && (CollisionTarget is null || CollisionTarget.Index > cellTarget.Index))
                {
                    Meshes = new Mesh[] { meshClash[0].MeshA, meshClash[0].MeshB };
                    CollisionTarget = cellTarget;
                    state.Break();
                }
            }
        });
    }
}

#endif
