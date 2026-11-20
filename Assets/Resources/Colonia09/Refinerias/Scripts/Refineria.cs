using UnityEngine;

namespace Colonia09
{
    [DisallowMultipleComponent]
    public sealed class Refineria : MonoBehaviour
    {
        [SerializeField, Min(0), Tooltip("Energía adicional al iniciar una misión con la bonificación de este nivel.")]
        private int energiaInicialDeMision;

        public int EnergiaInicialDeMision => energiaInicialDeMision;
    }
}

