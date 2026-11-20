using UnityEngine;

namespace Colonia09
{
    [DisallowMultipleComponent]
    public sealed class PlantaSolar : MonoBehaviour
    {
        [SerializeField, Min(0), Tooltip("Unidades de energía que produce este nivel en una hora de juego.")]
        private int energiaPorHora;

        public int EnergiaPorHora => energiaPorHora;
    }
}

