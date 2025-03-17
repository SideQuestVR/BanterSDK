#if BANTER_VISUAL_SCRIPTING
using Unity.VisualScripting;
using Banter.SDK;
using UnityEngine;

namespace Banter.VisualScripting
{
    /// <summary>
    /// Retrieves spectrum data from the AudioListener attached to the main camera.
    /// </summary>
    [UnitTitle("Audio: Get AudioListener Spectrum Data")]
    [UnitShortTitle("AudioListenerSpectrumData")]
    [UnitCategory("Banter\\Utils")]
    [TypeIcon(typeof(AudioListener))]
    public class AudioListenerSpectrumData : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        /// <summary>
        /// Number of samples for the spectrum data (e.g. 64, 128, 256, etc.).
        /// </summary>
        [DoNotSerialize]
        public ValueInput channels;

        /// <summary>
        /// FFTWindow type used for the spectrum analysis.
        /// </summary>
        [DoNotSerialize]
        public ValueInput window;

        /// <summary>
        /// Output array of float values representing the spectrum data.
        /// </summary>
        [DoNotSerialize]
        public ValueOutput spectrumData;

        // This private field will hold our spectrum data
        private float[] outputSpectrum;

        protected override void Definition()
        {
            inputTrigger = ControlInput("In", (flow) =>
            {
                // Get the number of samples and FFTWindow from the inputs.
                int numSamples = flow.GetValue<int>(channels);
                FFTWindow fftWindow = flow.GetValue<FFTWindow>(window);

                // Create the float array for the spectrum data.
                outputSpectrum = new float[numSamples];

                // Try to get the AudioListener from the main camera.
                if (Camera.main != null)
                {
                    AudioListener listener = Camera.main.GetComponent<AudioListener>();
                    if (listener != null)
                    {
                        // Get the spectrum data (using channel index 0).
                        AudioListener.GetSpectrumData(outputSpectrum, 0, fftWindow);
                    }
                    else
                    {
                        Debug.LogWarning("Main camera does not have an AudioListener component.");
                    }
                }
                else
                {
                    Debug.LogWarning("Main camera not found.");
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("Out");

            // Define inputs with default values.
            channels = ValueInput<int>("Channels", 64);
            window = ValueInput<FFTWindow>("Window", FFTWindow.Rectangular);

            // Define the output that returns the spectrum data array.
            spectrumData = ValueOutput<float[]>("SpectrumData", (flow) => outputSpectrum);

            // Ensure inputs are available before execution.
            Requirement(channels, inputTrigger);
            Requirement(window, inputTrigger);
            Assignment(inputTrigger, spectrumData);
        }
    }

    /// <summary>
    /// Retrieves spectrum data from a given AudioSource.
    /// </summary>
    [UnitTitle("Audio: Get AudioSource Spectrum Data")]
    [UnitShortTitle("AudioSourceSpectrumData")]
    [UnitCategory("Banter\\Utils")]
    [TypeIcon(typeof(AudioSource))]
     public class AudioSourceSpectrumData : Unit
    {
        [DoNotSerialize]
        public ControlInput inputTrigger;

        [DoNotSerialize]
        public ControlOutput outputTrigger;

        /// <summary>
        /// The GameObject that should have an AudioSource component. If left null, uses self.
        /// </summary>
        [DoNotSerialize]
        public ValueInput audioSource;

        /// <summary>
        /// Number of samples for the spectrum data (e.g. 64, 128, 256, etc.).
        /// </summary>
        [DoNotSerialize]
        public ValueInput channels;

        /// <summary>
        /// FFTWindow type used for the spectrum analysis.
        /// </summary>
        [DoNotSerialize]
        public ValueInput window;

        /// <summary>
        /// Output array of float values representing the spectrum data.
        /// </summary>
        [DoNotSerialize]
        public ValueOutput spectrumData;

        // Private field to hold the spectrum data.
        private float[] outputSpectrum;

        protected override void Definition()
        {
            inputTrigger = ControlInput("In", (flow) =>
            {
                // Use NullMeansSelf so that if no GameObject is provided, self is used.
                GameObject go = flow.GetValue<GameObject>(audioSource);
                AudioSource src = null;

                if (go != null)
                {
                    // Try to get the AudioSource from the provided GameObject.
                    src = go.GetComponent<AudioSource>();
                    if (src == null)
                    {
                        // Fallback: search in children.
                        src = go.GetComponentInChildren<AudioSource>();
                        if (src == null)
                        {
                            Debug.LogWarning("The provided GameObject does not have an AudioSource component. " + go.name);
                        }
                    }
                }
                else
                {
                    Debug.LogWarning("AudioSource input is null.");
                }

                // Get the number of samples and FFTWindow type from the inputs.
                int numSamples = flow.GetValue<int>(channels);
                FFTWindow fftWindow = flow.GetValue<FFTWindow>(window);

                // Prepare the float array for the spectrum data.
                outputSpectrum = new float[numSamples];

                if (src != null)
                {
                    // Retrieve spectrum data from the AudioSource on channel 0.
                    src.GetSpectrumData(outputSpectrum, 0, fftWindow);
                }

                return outputTrigger;
            });

            outputTrigger = ControlOutput("Out");

            // Define inputs with default values.
            // Use NullMeansSelf so that if no GameObject is provided, the "self" GameObject is used.
            audioSource = ValueInput<GameObject>("AudioSource", null).NullMeansSelf();
            channels = ValueInput<int>("Channels", 64);
            window = ValueInput<FFTWindow>("Window", FFTWindow.Rectangular);

            // Define the output that returns the spectrum data.
            spectrumData = ValueOutput<float[]>("SpectrumData", (flow) => outputSpectrum);

            // Set up the input requirements.
            Requirement(audioSource, inputTrigger);
            Requirement(channels, inputTrigger);
            Requirement(window, inputTrigger);
            Assignment(inputTrigger, spectrumData);
        }
    }
}
#endif