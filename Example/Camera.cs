using OpenTK.Platform;
using System.Numerics;
namespace Example;

sealed class Camera
{
    public bool KeepAspect { get; set; }
    public bool IsPerspective { get; set; } = true;
    public int Priority { get; set; }
    public Matrix4x4 ViewMatrix { get { return Matrix4x4.CreateLookAt(Transform.LocalPosition, Transform.LocalPosition + Transform.LocalForward, Transform.LocalUp); } }
    public Matrix4x4 PerspectiveMatrix { get { return ComputePerspective(); } }

    public Matrix4x4 OrthographicMatrix { get { return Matrix4x4.CreateOrthographicOffCenter(-64, 64, -64, 64, far, near); } }
    public Matrix4x4 ProjectionMatrix { get { return IsPerspective ? (PerspectiveMatrix * ViewMatrix) : (ViewMatrix * OrthographicMatrix); } }

    const float ToRadians = (MathF.PI / 180f);
    const float ToDegrees = (180f / MathF.PI);

    private int msaa = 1;
    public int MSAA
    {
        get => msaa; set
        {
            msaa = Math.Max(value, 1);
        }
    }

    private float fov = 60;
    public float AspectRatio { get; set; } = 1;
    private float near;
    private float far;

    Matrix4x4 ComputePerspective()
    {
        return Matrix4x4.CreatePerspectiveFieldOfView(ToRadians * FOV, AspectRatio, Near, Far);
        ////float f = 1.0f / MathF.Tan(MathHelper.DegreesToRadians(FOV) / 2.0f);
        //try
        //{
        //    float f = 1 / MathF.Tan(MathHelper.DegreesToRadians(FOV) / 2f);
        //    var result = new Matrix4(f / (viewport.Size.X / (float)viewport.Size.Y), 0, 0, 0,
        //        0, f, 0, 0,
        //        0, 0, 0, -1,
        //        0, 0, near, 0);
        //    //var result = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(FOV), RenderTexture.Width / (float)RenderTexture.Height, Near, Far);
        //    //result.Row2.X = 0;
        //    //result.Row2.Y = 0;
        //    //result.Row2.Z = (Near / (Far - Near));
        //    //result.Row3.Z = (Far * Near) / (Far - Near);
        //    return result;
        //}
        //catch
        //{
        //    return Matrix4.Identity;
        //}

    }

    Transform transform;
    public Transform Transform
    {
        get
        {
            return transform;
        }
    }

    public float Near
    {
        get => near; set
        {
            if (value <= 0)
            {
                return;
            }
            near = value;
        }
    }
    public float Far
    {
        get => far; set
        {
            if (value <= near)
            {
                return;
            }
            far = value;
        }
    }
    public float FOV
    {
        get => fov; set
        {
            fov = float.Clamp(value, 0.1f, 179.9f);
        }
    }

    public Camera()
    {
        near = 0.06f;
        far = 10000;
        FOV = 60f;
        //angleX = -90f * MathF.PI / 180f;
        parent = Transform.Create();
        parent.Position = new Vector3(512, 512, 256);
        transform = Transform.Create();
        transform.Parent = parent;
        //transform.Position = new Vector3(330.5f, 478.5f, 210.5f);
        transform.LocalPosition = new Vector3(0, 0, 250);
        //
        //transform.Position = new Vector3(252, 430, 421);
    }

    float angleX, angleY;
    private Transform parent = Transform.Create();
    bool freeMouse = true;
    float rotation = 0f;
    const float rotationSpeed = 360f / 10f;
    public void Update(Input input, float dt)
    {
        float speedRot = 3.4f;
        float speed = 100;
        if (input.KeyPressed(Scancode.Escape))
        {
            freeMouse = !freeMouse;
            if (freeMouse)
            {
                input.CursorCaptureMode = CursorCaptureMode.Normal;
            }
            else
            {
                input.CursorCaptureMode = CursorCaptureMode.Locked;
            }
        }
        if (!freeMouse)
        {
            angleX += dt * input.MouseDelta.X;
            angleY += dt * input.MouseDelta.Y;
        }
        else
        {
            //rotation += rotationSpeed * dt;
            //rotation %= 360f;
            //parent.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, ToRadians * rotation);
        }
        if (input.KeyHeld(Scancode.LeftControl))
        {
            speed = 200f;
        }
        var fwd = Transform.LocalForward;
        fwd = Vector3.Normalize(new Vector3(fwd.X, 0, fwd.Z));
        if (input.KeyHeld(Scancode.W))
        {
            Transform.LocalPosition -= fwd * speed * dt;
        }
        if (input.KeyHeld(Scancode.S))
        {
            Transform.LocalPosition += fwd * speed * dt;
        }
        if (input.KeyHeld(Scancode.A))
        {
            Transform.LocalPosition -= Transform.LocalRight * speed * dt * (Transform.LocalUp.Y < 0 ? -1 : 1);
        }
        if (input.KeyHeld(Scancode.D))
        {
            Transform.LocalPosition += Transform.LocalRight * speed * dt * (Transform.LocalUp.Y < 0 ? -1 : 1); ;
        }
        if (input.KeyHeld(Scancode.Spacebar))
        {
            Transform.LocalPosition += new Vector3(0, dt, 0) * speed;
        }
        if (input.KeyHeld(Scancode.LeftShift))
        {
            Transform.LocalPosition -= new Vector3(0, dt, 0) * speed;
        }

        if (input.KeyHeld(Scancode.LeftArrow))
        {
            angleX += dt * speedRot;
        }
        if (input.KeyHeld(Scancode.RightArrow))
        {
            angleX -= dt * speedRot;
        }
        if (input.KeyHeld(Scancode.DownArrow))
        {
            angleY -= dt * speedRot;
        }
        if (input.KeyHeld(Scancode.UpArrow))
        {
            angleY += dt * speedRot;
        }
        //if (input.MouseDelta != OpenTK.Mathematics.Vector2.Zero)
        //Console.WriteLine(input.MouseDelta);
        if (input.KeyHeld(Scancode.Return))
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        Transform.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, angleX) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, angleY);
    }
}
