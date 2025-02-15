using UnityEngine;

[ExecuteInEditMode]
public class SplineCurveSurface : Assembly
{
    [Header("References")]
    public SplineCurveDrawer[] splineCurveDrawers;
    public SceneRigidBody sceneRigidBody;

    public override void AssemblyUpdate()
    {
        if (sceneRigidBody == null)
        {
            return;
        }
        if (splineCurveDrawers.Length == 0) return;

        for (int i = 0; i < splineCurveDrawers.Length; i++)
        {
            if (splineCurveDrawers[i] == null) continue;

            Vector3[] splinePoints = splineCurveDrawers[i].CreateBoxSplinePoints(true, false);
            if (splinePoints == null || splinePoints.Length < 2)
            {
                Debug.LogWarning("Not enough spline points! SplineCurveSurface: " + this.name);
                return;
            }
            
            Vector2[] splinePoints2D = new Vector2[splinePoints.Length];
            for (int j = 0; j < splinePoints.Length; j++)
            {
                splinePoints2D[j] = new Vector2(splinePoints[j].x, splinePoints[j].y);
            }
            
            sceneRigidBody.OverridePolygonPoints(splinePoints2D, i);
        }
    }
}