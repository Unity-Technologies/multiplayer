using UnityEngine;
using Unity.Entities;

public struct BulletAgeComponentData : IComponentData
{
    public BulletAgeComponentData(float maxAge)
    {
        this.maxAge = maxAge;
        age = 0;
    }

    public float age;
    public float maxAge;
}
