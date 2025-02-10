using System.Numerics;
namespace BrickEngine.Example
{
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
        bool rotate = true;
        float rotation = 0f;
        const float rotationSpeed = 360f / 10f;
        public void Update(/*Input Input,*/ float dt)
        {
            float speedRot = 3.4f;
            float speed = 32f;
            //if (Input.GetKeyDown(Key.P))
            //{
            //    rotate = !rotate;
            //}
            if (rotate)
            {
                rotation += rotationSpeed * dt;
                rotation %= 360f;
                parent.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, ToRadians * rotation);
            }
            //if (Input.GetKey(Key.LeftControl))
            //{
            //    speed = 100f;
            //}
            var fwd = Transform.LocalForward;
            fwd = Vector3.Normalize(new Vector3(fwd.X, 0, fwd.Z));
            //if (Input.GetKey(Key.W))
            //{
            //    Transform.LocalPosition -= fwd * speed * dt;
            //}
            //if (Input.GetKey(Key.S))
            //{
            //    Transform.LocalPosition += fwd * speed * dt;
            //}
            //if (Input.GetKey(Key.A))
            //{
            //    Transform.LocalPosition -= Transform.LocalRight * speed * dt * (Transform.LocalUp.Y < 0 ? -1 : 1);
            //}
            //if (Input.GetKey(Key.D))
            //{
            //    Transform.LocalPosition += Transform.LocalRight * speed * dt * (Transform.LocalUp.Y < 0 ? -1 : 1); ;
            //}
            //if (Input.GetKey(Key.Space))
            //{
            //    Transform.LocalPosition += new Vector3(0, dt, 0) * speed;
            //}
            //if (Input.GetKey(Key.LeftShift))
            //{
            //    Transform.LocalPosition -= new Vector3(0, dt, 0) * speed;
            //}

            //if (Input.GetKey(Key.Left))
            //{
            //    angleX += dt * speedRot;
            //}
            //if (Input.GetKey(Key.Right))
            //{
            //    angleX -= dt * speedRot;
            //}
            //if (Input.GetKey(Key.Down))
            //{
            //    angleY -= dt * speedRot;
            //}
            //if (Input.GetKey(Key.Up))
            //{
            //    angleY += dt * speedRot;
            //}
            //if (Input.GetKey(Key.Return))
            //{
            //    GC.Collect();
            //    GC.WaitForPendingFinalizers();
            //}
            Transform.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, angleX) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, angleY);
        }
    }
}
