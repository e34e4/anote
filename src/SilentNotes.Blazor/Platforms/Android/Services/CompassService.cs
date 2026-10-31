// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using Android.Hardware;
using Android.Runtime;
using SilentNotes.Services;
using Sensor = Android.Hardware.Sensor;
using SensorType = Android.Hardware.SensorType;

namespace SilentNotes.Platforms.Services
{
    /// <summary>
    /// Android compass using the rotation vector when available, otherwise accelerometer + magnetometer.
    /// </summary>
    internal class CompassService : Java.Lang.Object, ICompassService, ISensorEventListener
    {
        private readonly IAppContextService _appContext;
        private SensorManager _sensorManager;
        private Sensor _rotation;
        private Sensor _accelerometer;
        private Sensor _magnetometer;
        private readonly float[] _gravity = new float[3];
        private readonly float[] _geomagnetic = new float[3];
        private readonly float[] _rotationMatrix = new float[9];
        private readonly float[] _orientation = new float[3];
        private bool _hasGravity;
        private bool _hasGeomagnetic;
        private bool _listening;
        private float? _heading;
        private string _accuracyText;

        public CompassService(IAppContextService appContext)
        {
            _appContext = appContext;
        }

        /// <inheritdoc/>
        public event Action ReadingChanged;

        /// <inheritdoc/>
        public bool IsSupported
        {
            get
            {
                EnsureSensors();
                return _rotation != null || (_accelerometer != null && _magnetometer != null);
            }
        }

        /// <inheritdoc/>
        public bool IsListening => _listening;

        /// <inheritdoc/>
        public float? HeadingDegrees => _heading;

        /// <inheritdoc/>
        public string AccuracyText => _accuracyText;

        /// <inheritdoc/>
        public void Start()
        {
            if (_listening || !IsSupported)
                return;

            EnsureSensors();
            if (_rotation != null)
            {
                _sensorManager.RegisterListener(this, _rotation, SensorDelay.Game);
            }
            else
            {
                _sensorManager.RegisterListener(this, _accelerometer, SensorDelay.Game);
                _sensorManager.RegisterListener(this, _magnetometer, SensorDelay.Game);
            }
            _listening = true;
        }

        /// <inheritdoc/>
        public void Stop()
        {
            if (!_listening || _sensorManager == null)
                return;
            try
            {
                _sensorManager.UnregisterListener(this);
            }
            catch (Exception)
            {
            }
            _listening = false;
        }

        private void EnsureSensors()
        {
            if (_sensorManager != null)
                return;
            var context = _appContext?.Context;
            if (context == null)
                return;
            _sensorManager = context.GetSystemService(global::Android.Content.Context.SensorService) as SensorManager;
            if (_sensorManager == null)
                return;
            _rotation = _sensorManager.GetDefaultSensor(SensorType.RotationVector);
            _accelerometer = _sensorManager.GetDefaultSensor(SensorType.Accelerometer);
            _magnetometer = _sensorManager.GetDefaultSensor(SensorType.MagneticField);
        }

        public void OnAccuracyChanged(Sensor sensor, [GeneratedEnum] SensorStatus accuracy)
        {
            _accuracyText = accuracy switch
            {
                SensorStatus.AccuracyHigh => "High accuracy",
                SensorStatus.AccuracyMedium => "Medium accuracy",
                SensorStatus.AccuracyLow => "Low accuracy — move device in a figure-eight",
                SensorStatus.Unreliable => "Unreliable — calibrate compass",
                _ => null,
            };
            ReadingChanged?.Invoke();
        }

        public void OnSensorChanged(SensorEvent e)
        {
            if (e?.Sensor == null || e.Values == null)
                return;

            if (e.Sensor.Type == SensorType.RotationVector)
            {
                SensorManager.GetRotationMatrixFromVector(_rotationMatrix, e.Values.ToArray());
                SensorManager.GetOrientation(_rotationMatrix, _orientation);
                UpdateHeading(_orientation[0]);
                return;
            }

            if (e.Sensor.Type == SensorType.Accelerometer)
            {
                LowPass(e.Values, _gravity);
                _hasGravity = true;
            }
            else if (e.Sensor.Type == SensorType.MagneticField)
            {
                LowPass(e.Values, _geomagnetic);
                _hasGeomagnetic = true;
            }

            if (!_hasGravity || !_hasGeomagnetic)
                return;

            if (!SensorManager.GetRotationMatrix(_rotationMatrix, null, _gravity, _geomagnetic))
                return;
            SensorManager.GetOrientation(_rotationMatrix, _orientation);
            UpdateHeading(_orientation[0]);
        }

        private void UpdateHeading(float azimuthRadians)
        {
            float degrees = (float)(azimuthRadians * (180.0 / Math.PI));
            if (degrees < 0)
                degrees += 360f;
            _heading = degrees;
            ReadingChanged?.Invoke();
        }

        private static void LowPass(System.Collections.Generic.IList<float> input, float[] output)
        {
            const float alpha = 0.15f;
            for (int i = 0; i < output.Length && i < input.Count; i++)
                output[i] = output[i] + alpha * (input[i] - output[i]);
        }
    }
}
