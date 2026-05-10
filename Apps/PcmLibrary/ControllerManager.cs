using PcmHacking.ECU;
using System;
using System.Collections.Generic;
using System.Text;

namespace PcmHacking
{
    public enum ControllerActions
    {
        Undefined = 0,
        Read,
        Write
    }

    /// <summary>
    /// How much of the PCM to erase and rewrite.
    /// </summary>
    public enum WriteType
    {
        None = 0,
        Compare,
        Test,
        Calibration,
        Parameters,
        OsPlusCalibrationPlusBoot,
        Full,
    }

    public interface IControllerManager
    {
        Task<bool> Begin(MemoryStream? contentStream);
        Task<bool> Begin(string path);
    }

    public class ControllerManager(Vehicle vehicle, ECUActionArguments actionArgs, ControllerPageObjects pageObjects, CancellationToken cancellationToken, IProgress<ProgressUpdate>? progress = null, ILogger? logger = null)
    {
        public ECUActionArguments ActionArgs = actionArgs;
        public ControllerPageObjects ControllerPageObjects = pageObjects;
        private Dictionary<ControllerActions, IControllerManager> _controllerActionLookup = [];
        private readonly Vehicle _vehicle = vehicle;
        private readonly CancellationToken _cancellationToken = cancellationToken;
        private readonly ILogger logger = logger;
        private readonly IProgress<ProgressUpdate> progress = progress;


        public void Initialize()
        {
            _controllerActionLookup = new Dictionary<ControllerActions, IControllerManager> {
                { ControllerActions.Read, new ReadManager(logger, _vehicle, ActionArgs, ControllerPageObjects, _cancellationToken, progress) },
                { ControllerActions.Write, new WriteManager(logger, _vehicle, ActionArgs, ControllerPageObjects, _cancellationToken, progress) }
            };                
        }

        public async Task<bool> BeginAction()
        {
            if (ActionArgs.PreFlightChecksRequired)
            {
                throw new Exception("Caller should use GetPreFlightCheckResult to prompt users before starting action!");
            }
            IControllerManager? selectedManager = null;
            if (_controllerActionLookup.TryGetValue(ActionArgs.SelectedAction, out selectedManager))
            {
                if (ActionArgs.SelectedAction == ControllerActions.Read && ActionArgs.ContentStream == null)
                {
                    ActionArgs.ContentStream = new MemoryStream(1208800);
                }
                return await selectedManager.Begin(ActionArgs.ContentStream);
            }
            return false;
        }
    }
}
