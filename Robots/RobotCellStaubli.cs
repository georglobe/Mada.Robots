﻿using System.Linq;
using System.IO;
using System.Collections.Generic;
using Rhino.Geometry;
using static System.Math;
using static Robots.Util;

namespace Robots
{
    public class RobotCellStaubli : RobotCell
    {
        internal RobotCellStaubli(string name, List<MechanicalGroup> mechanicalGroups, IO io, Plane basePlane, Mesh environment) : base(name, Manufacturers.Staubli, mechanicalGroups, io, basePlane, environment)
        {
        }

        public static Plane EulerToPlane(double x, double y, double z, double aDeg, double bDeg, double cDeg)
        {
            double a = aDeg.ToRadians();
            double b = bDeg.ToRadians();
            double c = cDeg.ToRadians();
            double ca = Cos(a);
            double sa = Sin(a);
            double cb = Cos(b);
            double sb = Sin(b);
            double cc = Cos(c);
            double sc = Sin(c);
            var tt = new Transform(1);
            tt[0, 0] = cb * cc; tt[0, 1] = ca * sc + sa * sb * cc; tt[0, 2] = sa * sc - ca * sb * cc;
            tt[1, 0] = -cb * sc; tt[1, 1] = ca * cc - sa * sb * sc; tt[1, 2] = sa * cc + ca * sb * sc;
            tt[2, 0] = sb; tt[2, 1] = -sa * cb; tt[2, 2] = ca * cb;

            var plane = tt.ToPlane();
            plane.Origin = new Point3d(x, y, z);
            return plane;
        }

        public static double[] PlaneToEuler(Plane plane)
        {
            Transform matrix = Transform.PlaneToPlane(Plane.WorldXY, plane);
            double a = Atan2(-matrix.M12, matrix.M22);
            double mult = 1.0 - matrix.M02 * matrix.M02;
            if (Abs(mult) < UnitTol) mult = 0.0;
            double b = Atan2(matrix.M02, Sqrt(mult));
            double c = Atan2(-matrix.M01, matrix.M00);

            if (matrix.M02 < (-1.0 + UnitTol))
            {
                a = Atan2(matrix.M21, matrix.M11);
                b = -PI / 2;
                c = 0;
            }
            else if (matrix.M02 > (1.0 - UnitTol))
            {
                a = Atan2(matrix.M21, matrix.M11);
                b = PI / 2;
                c = 0;
            }

            return new double[] { plane.OriginX, plane.OriginY, plane.OriginZ, a.ToDegrees(), b.ToDegrees(), c.ToDegrees() };
        }

        public override double[] PlaneToNumbers(Plane plane) => PlaneToEuler(plane);
        public override Plane NumbersToPlane(double[] numbers) => EulerToPlane(numbers[0], numbers[1], numbers[2], numbers[3], numbers[4], numbers[5]);

        internal override void SaveCode(Program program, string folder)
        {
            if (program.Code == null) return;

            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($" Folder \"{folder}\" not found");
            var programDir = $@"{folder}\{program.Name}";
            Directory.CreateDirectory(programDir);

            for (int i = 0; i < program.Code.Count; i++)
            {
                string group = MechanicalGroups[i].Name;
                string programName = $"{program.Name}_{group}";

                for (int j = 0; j < program.Code[i].Count; j++)
                {
                    string name;

                    switch (j)
                    {
                        case 0:
                            name = $"{programName}.pjx"; break;
                        case 1:
                            name = $"{programName}.dtx"; break;
                        case 2:
                            name = "start.pgx"; break;
                        case 3:
                            name = "stop.pgx"; break;
                        default:
                            name = $"{programName}_{j - 4:000}.pgx"; break;
                    }

                    string file = $@"{programDir}\{name}";
                    var joinedCode = string.Join("\r\n", program.Code[i][j]);
                    File.WriteAllText(file, joinedCode);
                }
            }
        }

        internal override List<List<List<string>>> Code(Program program) => new VAL3PostProcessor(this, program).Code;

        internal static class VAL3Syntax
        {
            public static string Data(string name, string type, string value = null, int size = 1)
            {
                string attribute = $@"    <Data name=""{name}"" access=""private"" xsi:type=""array"" type=""{type}"" size=""{size}"">
      <Value key=""0"" {value} />
    </Data>";

                return attribute;
            }

            public static string NumData(string name, double number)
            {
                string value = $@"value=""{number:0.###}""";
                return Data(name, "num", value);
            }

            public static string TrsfData(string name, Plane plane)
            {
                var values = PlaneToEuler(plane);
                string value = $@"x =""{values[0]:0.###}"" y =""{values[1]:0.###}"" z =""{values[2]:0.###}"" rx =""{values[3]:0.####}"" ry =""{values[4]:0.####}"" rz =""{values[5]:0.####}""";
                return Data(name, "trsf", value);
            }

            public static string Local(string name, string type, int size = 1)
            {
                return $@"      <Local name=""{name}"" type=""{type}"" xsi:type=""array"" size=""{size}"" />";
            }
        }

        class VAL3PostProcessor
        {
            readonly RobotCellStaubli _cell;
            readonly Program _program;

            public List<List<List<string>>> Code { get; }

            internal VAL3PostProcessor(RobotCellStaubli robotCell, Program program)
            {
                _cell = robotCell;
                _program = program;

                if (!CheckNames()) return;

                Code = new List<List<List<string>>>();

                for (int i = 0; i < _cell.MechanicalGroups.Count; i++)
                {
                    var group = _cell.MechanicalGroups[i];
                    var name = $"{_program.Name}_{group.Name}";
                    var mdescs = CreateMdescs(i);

                    var groupCode = new List<List<string>>
                    {
                        Program(name),
                        DataList(mdescs),
                        Start(name),
                        Stop(name),
                    };

                    for (int j = 0; j < program.MultiFileIndices.Count; j++)
                        groupCode.Add(SubModule(j, i, mdescs));

                    Code.Add(groupCode);
                }
            }

            bool CheckNames()
            {
                foreach (var group in _cell.MechanicalGroups)
                {
                    var name = $"{_program.Name}_{group.Name}";

                    if (name.Length >= 12)
                    {
                        _program.Errors.Add($"Program name combined with mechanical group name '{name}' is too long, should be shorter than 16 characters.");
                        return false;
                    }
                }

                foreach (var attribute in _program.Attributes)
                {
                    int maxLength = 16;

                    if (attribute is Tool)
                        maxLength = 14;

                    if (attribute.Name.Length >= maxLength)
                    {
                        _program.Errors.Add($"Attribute name '{attribute.Name}' is too long, should be shorter than {maxLength} characters.");
                        return false;
                    }
                }

                return true;
            }

            Dictionary<(Speed speed, Zone zone), string> CreateMdescs(int group)
            {
                var mdescs = new Dictionary<(Speed speed, Zone zone), string>();
                int count = 0;

                foreach (var cellTarget in _program.Targets)
                {
                    var target = cellTarget.ProgramTargets[group].Target;
                    var key = (target.Speed, target.Zone);

                    if (!mdescs.TryGetValue(key, out var value))
                    {
                        string name = $"mdesc{count:0000}";
                        mdescs.Add(key, name);
                        count++;
                    }
                }

                return mdescs;
            }

            List<string> Program(string name)
            {
                var codes = new List<string>();

                string start = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<Project xmlns=""http://www.staubli.com/robotics/VAL3/Project/3"">
  <Parameters version=""s7.10.2"" stackSize=""5000"" millimeterUnit=""true"" />
  <Programs>
    <Program file=""start.pgx"" />
    <Program file=""stop.pgx"" />";

                codes.Add(start);

                for (int j = 0; j < _program.MultiFileIndices.Count; j++)
                {
                    codes.Add($@"    <Program file=""{name}_{j:000}.pgx"" />");
                }

                string end = $@" </Programs>
  <Database>
    <Data file=""{name}.dtx"" />
  </Database>
  <Libraries />
</Project>
    ";
                codes.Add(end);
                return codes;
            }

            string ProgramHeader(string name)
            {
                return $@"<?xml version=""1.0"" encoding=""utf-8"" ?>
<Programs xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns=""http://www.staubli.com/robotics/VAL3/Program/2"" >
  <Program name=""{name}"">";
            }

            string ProgramFooter()
            {
                return @"end]]></Code>
  </Program>
</Programs>";
            }

            List<string> Start(string name)
            {
                var codes = new List<string>();

                string start = $@"{ProgramHeader("start")}
    <Locals>
    </Locals>
    <Code><![CDATA[begin
cls()
putln(""Program '{name}' started..."")";

                codes.Add(start);

                foreach (var command in _program.InitCommands)
                    codes.Add(command.Code(_program, Target.Default));

                for (int j = 0; j < _program.MultiFileIndices.Count; j++)
                    codes.Add($"call {name}_{j:000}()");

                string end = $@"waitEndMove()
{ProgramFooter()}";

                codes.Add(end);
                return codes;
            }

            List<string> Stop(string name)
            {
                var codes = new List<string>();

                string start = $@"{ProgramHeader("stop")}
    <Locals>
    </Locals>
    <Code><![CDATA[begin
putln(""Program '{name}' stopped."")";

                codes.Add(start);
                codes.Add(ProgramFooter());
                return codes;
            }

            List<string> DataList(Dictionary<(Speed speed, Zone zone), string> mdescs)
            {
                var codes = new List<string>();

                string start = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<Database xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns=""http://www.staubli.com/robotics/VAL3/Data/2"">
  <Datas>";

                codes.Add(start);
                var attributes = _program.Attributes;

                VAL3Syntax.NumData("Inertia", 0);
                foreach (var tool in attributes.OfType<Tool>()) codes.Add(Tool(tool));
                foreach (var frame in attributes.OfType<Frame>()) codes.Add(Frame(frame));

                codes.AddRange(Speeds(mdescs));

                foreach (var command in attributes.OfType<Command>())
                {
                    string declaration = command.Declaration(_program);
                    if (declaration != null)
                        codes.Add(declaration);
                }

                string end = $@"  </Datas>
</Database>";

                codes.Add(end);
                return codes;
            }

            string Tool(Tool tool)
            {
                var values = _cell.PlaneToNumbers(tool.Tcp);
                double weight = (tool.Weight > 0.001) ? tool.Weight : 0.001;

                Point3d centroid = tool.Centroid;
                if (centroid.DistanceTo(Point3d.Origin) < 0.001)
                    centroid = new Point3d(0, 0, 0.001);

                string toolText = VAL3Syntax.Data(tool.Name, "tool", $@"x= ""{values[0]:0.###}"" y=""{values[1]:0.###}"" z=""{values[2]:0.###}"" rx=""{values[3]:0.####}"" ry=""{values[4]:0.####}"" rz=""{values[5]:0.####}"" fatherId=""flange[0]""");
                string centroidText = VAL3Syntax.Data($"{tool.Name}_C", "trsf", $@"x=""{centroid.X:0.###}"" y=""{centroid.Y:0.###}"" z=""{centroid.Z:0.###}""");
                string weightText = VAL3Syntax.NumData($"{tool.Name}_W", weight);

                return $"{toolText}\r\n{centroidText}\r\n{weightText}";
            }

            string Frame(Frame frame)
            {
                if (frame.IsCoupled)
                {
                    _program.Warnings.Add(" Frame coupling not supported with Staubli robots.");
                }

                Plane plane = frame.Plane;
                plane.Transform(Transform.PlaneToPlane(_cell.BasePlane, Plane.WorldXY));
                var values = _cell.PlaneToNumbers(plane);

                string code = VAL3Syntax.Data(frame.Name, "frame", $@"x=""{values[0]:0.###}"" y=""{values[1]:0.###}"" z=""{values[2]:0.###}"" rx=""{values[3]:0.####}"" ry=""{values[4]:0.####}"" rz=""{values[5]:0.####}"" fatherId=""world[0]""");
                return code;
            }

            List<string> Speeds(Dictionary<(Speed speed, Zone zone), string> mdescs)
            {
                var codes = new List<string>();

                foreach (var pair in mdescs)
                {
                    var mdesc = pair.Key;
                    var name = pair.Value;
                    var speed = mdesc.speed.TranslationSpeed;
                    double rotation = mdesc.speed.RotationSpeed.ToDegrees();
                    var blend = mdesc.zone.IsFlyBy ? "Cartesian" : "off";
                    var zone = mdesc.zone.Distance;

                    string code = VAL3Syntax.Data(name, "mdesc", $@"accel=""100"" vel=""100"" decel=""100"" tmax=""{speed:0.###}"" rmax=""{rotation:0.###}"" blend=""{blend}"" leave=""{zone}"" reach=""{zone}""");
                    codes.Add(code);
                }

                return codes;
            }

            List<string> SubModule(int file, int group, Dictionary<(Speed speed, Zone zone), string> mdescs)
            {
                string groupName = _cell.MechanicalGroups[group].Name;

                int start = _program.MultiFileIndices[file];
                int end = (file == _program.MultiFileIndices.Count - 1) ? _program.Targets.Count : _program.MultiFileIndices[file + 1];

                Tool lastTool = null;
                int jointCount = 0;
                int pointCount = 0;

                var instructions = new List<string>();

                for (int j = start; j < end; j++)
                {
                    var programTarget = _program.Targets[j].ProgramTargets[group];
                    var target = programTarget.Target;

                    var tool = target.Tool.Name;
                    var key = (target.Speed, target.Zone);
                    var speed = mdescs[key];
                    string moveText = "";

                    // payload
                    if (lastTool == null || target.Tool != lastTool)
                    {
                        instructions.Add($"setPayload({tool}, {tool}_W, {tool}_C, Inertia)");
                        lastTool = target.Tool;
                    }

                    // external
                    if (_cell.MechanicalGroups[group].Externals.Count > 0)
                    {
                        _program.Warnings.Add("External axes not implemented in Staubli.");
                    }

                    if (programTarget.IsJointTarget)
                    {
                        string targetName = $"joints[{jointCount++}]";
                        var jointTarget = programTarget.Target as JointTarget;

                        double[] joints = jointTarget.Joints;
                        joints = joints.Select((x, i) => _cell.MechanicalGroups[group].RadianToDegree(x, i)).ToArray();

                        var assignment = $"{targetName} = {{{joints[0]:0.####}, {joints[1]:0.####}, {joints[2]:0.####}, {joints[3]:0.####}, {joints[4]:0.####}, {joints[5]:0.####}}}";
                        var command = $"movej({targetName}, {tool}, {speed})";
                        moveText = $"{assignment}\r\n{command}";
                    }
                    else
                    {
                        string targetName = $"points[{pointCount++}]";
                        var cartesian = programTarget.Target as CartesianTarget;

                        string shoulderT = "ssame";
                        string elbowT = "esame";
                        string wristT = "wsame";

                        string move = "";

                        switch (cartesian.Motion)
                        {
                            case Motions.Joint:
                                {
                                    RobotConfigurations configuration = programTarget.Kinematics.Configuration;
                                    bool shoulder = configuration.HasFlag(RobotConfigurations.Shoulder);
                                    bool elbow = configuration.HasFlag(RobotConfigurations.Elbow);
                                    if (shoulder) elbow = !elbow;
                                    bool wrist = configuration.HasFlag(RobotConfigurations.Wrist);

                                    wristT = !wrist ? "wpositive" : "wnegative";
                                    elbowT = !elbow ? "epositive" : "enegative";
                                    shoulderT = !shoulder ? "lefty" : "righty";

                                    move = "movej";
                                    break;
                                }

                            case Motions.Linear:
                                {
                                    move = "movel";
                                    break;
                                }

                            default:
                                {
                                    _program.Warnings.Add($" Movement type '{cartesian.Motion} not supported.");
                                    continue;
                                }
                        }

                        var values = _cell.PlaneToNumbers(cartesian.Plane);
                        string localtrsf = $"{{{values[0]:0.###}, {values[1]:0.###}, {values[2]:0.###}, {values[3]:0.####}, {values[4]:0.####}, {values[5]:0.####}}}";

                        string config = $"{{{shoulderT}, {elbowT}, {wristT}}}";

                        string point = $"{targetName} = {{{localtrsf}, {config}}}";
                        string link = $"link({targetName}, {target.Frame.Name})";

                        moveText = $"{point}\r\n{link}\r\n{move}({targetName}, {tool}, {speed})";
                    }

                    foreach (var command in programTarget.Commands.Where(c => c.RunBefore))
                        instructions.Add(command.Code(_program, target));

                    instructions.Add(moveText);

                    foreach (var command in programTarget.Commands.Where(c => !c.RunBefore))
                        instructions.Add(command.Code(_program, target));
                }

                var locals = new List<string>();

                if (jointCount > 0)
                    locals.Add(VAL3Syntax.Local("joints", "joint", jointCount));
                if (pointCount > 0)
                    locals.Add(VAL3Syntax.Local("points", "point", pointCount));

                string programName = $"{_program.Name}_{groupName}_{file: 000}";
                string startCode = $@"{ProgramHeader(programName)}
    <Locals>";

                string midCode = @"    </Locals>
    <Code><![CDATA[begin ";

                var code = new List<string>();
                code.Add(startCode);
                code.AddRange(locals);
                code.Add(midCode);
                code.AddRange(instructions);
                code.Add(ProgramFooter());
                return code;
            }
        }
    }
}