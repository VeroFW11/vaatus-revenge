using System;
using System.Numerics;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // Screen shake must die away on time, stay within its limits and never produce garbage angles.
    public class CameraShakeTests
    {
        const float Frame = 1f / 60f;
        const float Frequency = 18f;
        const float MaxAngle = 8f;

        [Test]
        public void ShakeFadesOutOverItsDuration()
        {
            var shake = new CameraShakeModel();
            shake.Add(0.3f, 0.5f);
            Assert.That(shake.Strength, Is.EqualTo(0.3f).Within(1e-5f), "full strength straight away: the impact frame");

            float previous = shake.Strength;
            for (int i = 0; i < 30; i++)
            {
                shake.Tick(Frame);
                Assert.LessOrEqual(shake.Strength, previous + 1e-6f, "only ever fades");
                previous = shake.Strength;
            }
            Assert.That(shake.Strength, Is.EqualTo(0f).Within(1e-4f));
            shake.Tick(Frame);
            Assert.AreEqual(0f, shake.Strength);
            Assert.AreEqual(Vector3.Zero, shake.SampleAngles(Frequency, MaxAngle));
        }

        [Test]
        public void TheSameShakesGiveTheSameWobble()
        {
            var a = new CameraShakeModel();
            var b = new CameraShakeModel();
            a.Add(0.2f, 1f);
            b.Add(0.2f, 1f);
            for (int i = 0; i < 40; i++)
            {
                a.Tick(Frame);
                b.Tick(Frame);
                Assert.AreEqual(a.SampleAngles(Frequency, MaxAngle), b.SampleAngles(Frequency, MaxAngle));
            }
        }

        [Test]
        public void WobbleStaysWithinTheMaximumAngle()
        {
            var shake = new CameraShakeModel();
            shake.Add(1f, 2f);
            bool moved = false;
            for (int i = 0; i < 120; i++)
            {
                shake.Tick(Frame);
                Vector3 angles = shake.SampleAngles(Frequency, MaxAngle);
                Assert.LessOrEqual(Math.Abs(angles.X), MaxAngle + 1e-4f);
                Assert.LessOrEqual(Math.Abs(angles.Y), MaxAngle + 1e-4f);
                Assert.LessOrEqual(Math.Abs(angles.Z), MaxAngle + 1e-4f);
                if (angles.LengthSquared() > 0.01f) moved = true;
            }
            Assert.IsTrue(moved, "it actually shakes");
        }

        [Test]
        public void StackedShakesAreCapped()
        {
            var shake = new CameraShakeModel();
            for (int i = 0; i < 20; i++) shake.Add(0.5f, 1f);
            Assert.LessOrEqual(shake.Strength, 1f);
            shake.Tick(Frame);
            Assert.LessOrEqual(shake.Strength, 1f);
        }

        [Test]
        public void BadValuesAreIgnored()
        {
            var shake = new CameraShakeModel();
            shake.Add(float.NaN, 1f);
            shake.Add(0.2f, 0f);
            shake.Add(-1f, 1f);
            shake.Add(0.2f, float.NaN);
            shake.Add(float.PositiveInfinity, 1f);
            shake.Add(0.2f, float.PositiveInfinity);
            Assert.AreEqual(0f, shake.Strength);

            shake.Add(0.2f, 1f);
            shake.Tick(float.NaN);
            shake.Tick(-1f);
            Assert.That(shake.Strength, Is.EqualTo(0.2f).Within(1e-5f), "bad time steps don't advance it");
            Assert.AreEqual(Vector3.Zero, shake.SampleAngles(float.NaN, MaxAngle));

            shake.Clear();
            Assert.AreEqual(0f, shake.Strength);
        }

        [Test]
        public void NoiseIsSmoothAndInRange()
        {
            for (int channel = 0; channel < 3; channel++)
            {
                float previous = CameraShakeModel.Noise(0f, channel);
                for (int i = 1; i <= 10000; i++)
                {
                    float value = CameraShakeModel.Noise(i * 0.01f, channel);
                    Assert.LessOrEqual(Math.Abs(value), 1f);
                    Assert.Less(Math.Abs(value - previous), 0.1f, "no sudden jumps");
                    previous = value;
                }
            }
            Assert.AreEqual(0f, CameraShakeModel.Noise(float.NaN, 0));
        }
    }
}
