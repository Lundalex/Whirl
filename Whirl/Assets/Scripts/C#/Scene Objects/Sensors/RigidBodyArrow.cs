using System;
using Resources2;
using Unity.Mathematics;
using UnityEngine;
using PM = ProgramManager;

public class RigidBodyArrow : SensorBase
{
    [Header("Primary Customizations")]
    [SerializeField] private RigidBodyArrowType rigidBodyArrowType;
    [SerializeField] private bool doDisplayValueBox;

    [Header("Interpolation")]
    [SerializeField] private bool doInterpolation = true;
    [SerializeField, Range(10.0f, 200.0f)] private float moveSpeed;
    [SerializeField, Range(1.0f, 100.0f)] private float rotationSpeed;
    [SerializeField, Range(1.0f, 20.0f)] private float valueLerpSpeed;

    [Header("Smart Interpolation")]
    [SerializeField] bool doPredictTransform;
    [SerializeField, Range(0.0f, 180f)] private float rotationLerpSkipThreshold;
    [SerializeField] private float minArrowValue;
    [SerializeField] private float maxArrowValue;
    [SerializeField] private float minArrowLength;
    [SerializeField] private float maxArrowLength;
    [SerializeField] private float minValueChangeForUpdate;

    [Header("Starting Values")]
    [SerializeField, Range(0.0f, 360.0f)] private float startRotation;

    [Header("Prefab reference")]
    [SerializeField] private GameObject uiArrowPrefab;

    // UIArrow
    private UIArrow uiArrow;

    // Private references
    private ArrowManager arrowManager;
    private Main main;
    private SensorManager sensorManager;

    [NonSerialized] public int linkedRBIndex = -1;
    private Vector2 canvasResolution;
    private Vector2 boundaryDims = Vector2.zero;
    private Vector2 currentTargetPosition;
    private float currentTargetRotation;

    // Predictions
    private Vector2 lastValue;
    private float lastSensorUpdateTime;
    private Vector2 lastVel;

    private Vector2 currentAcc;
    private Vector2 targetAcc;

    [NonSerialized] public bool firstDataRecieved = false;

    public void Initialize()
    {
        uiArrow = arrowManager.CreateArrow(uiArrowPrefab);
        uiArrow.UpdateArrow(0f, "", 0, 0f);
        uiArrow.SetArrowVisibility(false);
        uiArrow.SetValueBoxVisibility(doDisplayValueBox);
        firstDataRecieved = false;

        SetConstants();

        PM.Instance.AddRigidBodyArrow(this);
    }

    public void SetReferences(ArrowManager arrowManager, Main main, SensorManager sensorManager)
    {
        this.arrowManager = arrowManager;
        this.main = main;
        this.sensorManager = sensorManager;
        this.canvasResolution = Func.Int2ToVector2(main.Resolution);
    }

    private void SetConstants()
    {
        
    }

    public void UpdateScript()
    {
        UpdatePosition();
    }

    private void UpdatePosition()
    {
        if (firstDataRecieved)
        {
            (Vector2 currentPosition, float currentRotation) = uiArrow.GetPosition();
            Vector2 canvasTargetPosition = SimSpaceToCanvasSpace(currentTargetPosition);
            float angleDiff = Mathf.Abs(Mathf.DeltaAngle(currentRotation, currentTargetRotation));

            currentPosition = doInterpolation
                ? Vector2.Lerp(currentPosition, canvasTargetPosition, PM.Instance.clampedDeltaTime * moveSpeed)
                : canvasTargetPosition;

            if (angleDiff > rotationLerpSkipThreshold) 
                currentRotation = currentTargetRotation;
            else 
                currentRotation = Mathf.LerpAngle(currentRotation, currentTargetRotation, PM.Instance.clampedDeltaTime * rotationSpeed);

            uiArrow.SetPosition(currentPosition, currentRotation);
        }
    }

    public override void UpdateSensor()
    {
        if (uiArrow == null) return;

        if (linkedRBIndex == -1)
        {
            Debug.LogWarning("Arrow not linked to any rigid body; It will not be updated. RigidBodySensor: " + this.name);
            return;
        }

        RBData[] retrievedRBDatas = sensorManager.retrievedRBDatas;
        RBData rbData = retrievedRBDatas[linkedRBIndex];

        Vector2 vel = Func.Int2ToFloat2(rbData.vel_AsInt2, main.FloatIntPrecisionRB);

        float sensorDt = sensorManager.msRigidBodyDataRetrievalInterval * 0.001f;
        float programDt = PM.Instance.totalScaledTimeElapsed - lastSensorUpdateTime;
        (Vector2 newValue, string unit) = GetDisplayInfo(retrievedRBDatas, linkedRBIndex, sensorDt, programDt);

        // Use current data for position & rotation targets
        currentTargetRotation = Func.AngleFromDir(newValue);
        currentTargetPosition = rbData.pos;

        // Prediction of position & rotation
        if (firstDataRecieved && doPredictTransform)
        {
            float oldAngle = Func.AngleFromDir(lastValue);
            float deltaAngle = Mathf.DeltaAngle(oldAngle, currentTargetRotation);

            // Position
            currentTargetPosition += sensorDt * vel;

            // Rotation
            currentTargetRotation += sensorDt * deltaAngle;
        }

        if (!firstDataRecieved)
        {
            uiArrow.SetPosition(SimSpaceToCanvasSpace(currentTargetPosition), startRotation);
            firstDataRecieved = true;
        }
        
        bool valueIsNearZero = newValue.magnitude < minArrowValue;
        bool rbIsbeingDragged = Func.IsBitSet(rbData.stateFlags, 0); // Check isBeingDragged flag bit
        if (valueIsNearZero || rbIsbeingDragged)
        {
            newValue = Vector2.zero;
            firstDataRecieved = false;
        }
        else if (Vector2.Distance(lastValue, newValue) < minValueChangeForUpdate) return;

        float newValueMgn = newValue == Vector2.zero ? 0.0f : newValue.magnitude;
        float factor = Mathf.Clamp01((newValueMgn - minArrowValue) / Mathf.Max(maxArrowValue - minArrowValue, 0.001f));
        float arrowLength = Mathf.Lerp(minArrowLength, maxArrowLength, factor);

        uiArrow.UpdateArrow(newValueMgn, unit, arrowLength, factor);

        // Store references for next frame
        lastValue = newValue;
    }

    private (Vector2 newValue, string unit) GetDisplayInfo(RBData[] rBDatas, int linkedRBIndex, float sensorDt, float programDt)
    {
        RBData rbData = rBDatas[linkedRBIndex];

        float simUnitToMetersFactor = main.SimUnitToMetersFactor;

        float mass = rbData.mass * 0.001f; // g -> kg
        Vector2 vel = Func.Int2ToFloat2(rbData.vel_AsInt2, main.FloatIntPrecisionRB) * simUnitToMetersFactor;
        Vector2 momentum = vel * mass;
        if (programDt != 0) targetAcc = (vel - lastVel) / programDt;
        lastSensorUpdateTime = PM.Instance.totalScaledTimeElapsed;
        lastVel = vel;
        currentAcc = doInterpolation ? Vector2.Lerp(currentAcc, targetAcc, PM.Instance.clampedDeltaTime * valueLerpSpeed) : targetAcc;

        Vector2 totalForce = currentAcc * rbData.mass;
        Vector2 springForce = rbData.recordedSpringForce;
        Vector2 frictionForce = rbData.recordedFrictionForce;

        Vector2 value = Vector2.zero;
        string unit = "";
        switch (rigidBodyArrowType)
        {
            case RigidBodyArrowType.Velocity:
                value = vel;
                unit = "m/s";
                break;

            case RigidBodyArrowType.Velocity_X:
                value = new(vel.x, 0);
                unit = "m/s";
                break;

            case RigidBodyArrowType.Velocity_Y:
                value = new(0, vel.y);
                unit = "m/s";
                break;

            case RigidBodyArrowType.Acceleration:
                value = currentAcc;
                unit = "m/s<sup>2</sup>";
                break;

            case RigidBodyArrowType.Acceleration_X:
                value = new(currentAcc.x, 0);
                unit = "m/s<sup>2</sup>";
                break;

            case RigidBodyArrowType.Acceleration_Y:
                value = new(0, currentAcc.y);
                unit = "m/s<sup>2</sup>";
                break;

            case RigidBodyArrowType.Momentum:
                value = momentum;
                unit = "Ns";
                break;

            case RigidBodyArrowType.Momentum_X:
                value = new(momentum.x, 0);
                unit = "Ns";
                break;

            case RigidBodyArrowType.Momentum_Y:
                value = new(0, momentum.y);
                unit = "Ns";
                break;

            case RigidBodyArrowType.TotalForce:
                value = totalForce;
                unit = "N";
                break;

            case RigidBodyArrowType.TotalForce_X:
                value = new(totalForce.x, 0);
                unit = "N";
                break;

            case RigidBodyArrowType.TotalForce_Y:
                value = new(0, totalForce.y);
                unit = "N";
                break;

            case RigidBodyArrowType.SpringForce:
                value = springForce;
                unit = "N";
                break;

            case RigidBodyArrowType.SpringForce_X:
                value = new(springForce.x, 0);
                unit = "N";
                break;

            case RigidBodyArrowType.SpringForce_Y:
                value = new(0, springForce.y);
                unit = "N";
                break;

            case RigidBodyArrowType.FrictionForce:
                value = frictionForce;
                unit = "N";
                break;

            case RigidBodyArrowType.FrictionForce_X:
                value = new(frictionForce.x, 0);
                unit = "N";
                break;

            case RigidBodyArrowType.FrictionForce_Y:
                value = new(0, frictionForce.y);
                unit = "N";
                break;

            case RigidBodyArrowType.GravityForce:
                value = new(0, -rbData.gravity);
                unit = "N";
                break;

            default:
                Debug.LogWarning("Unrecognised RigidBodyArrowType: " + this.name);
                break;
        }

        return (value, unit);
    }

    private Vector2 SimSpaceToCanvasSpace(Vector2 simCoords)
        => (simCoords / GetBoundaryDims() - new Vector2(0.5f, 0.5f)) * canvasResolution;

    public Vector2 GetBoundaryDims()
    {
        if (boundaryDims == Vector2.zero)
        {
            int2 boundaryDimsInt2 = PM.Instance.main.BoundaryDims;
            boundaryDims = new(boundaryDimsInt2.x, boundaryDimsInt2.y);
        }
        return boundaryDims;
    }
}
