﻿using Lumia.Imaging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using VideoEffects;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media.Capture;
using Windows.Media.Devices;
using Windows.Media.MediaProperties;
using Windows.Phone.UI.Input;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Windows.Web.Http;
using ZXing;
using ZXing.Common;

// Die Elementvorlage "Leere Seite" ist unter http://go.microsoft.com/fwlink/?LinkID=390556 dokumentiert.

namespace CampusAppEvalWP
{
    /// <summary>
    /// Eine leere Seite, die eigenständig verwendet werden kann oder auf die innerhalb eines Rahmens navigiert werden kann.
    /// </summary>
    public sealed partial class QR_Code : Page
    {          
        MediaCapture mediaCapture;
        BarcodeReader reader;
        FocusControl focusControl;
        DispatcherTimer dispatcherTimer;

        private bool closeApp;
        private bool OpenWindow;

        public QR_Code()
        {
            this.InitializeComponent();

            var appView = Windows.UI.ViewManagement.ApplicationView.GetForCurrentView();
            appView.SetDesiredBoundsMode(ApplicationViewBoundsMode.UseCoreWindow);

            //Application.Current.Resuming += Application_Resuming;
            //Application.Current.Suspending += Application_Suspending;

            Application.Current.Suspending += new SuspendingEventHandler(App_Suspending);
            Application.Current.Resuming += new EventHandler<Object>(App_Resuming);

            disableSearchFrame();

            this.closeApp = false;
            this.OpenWindow = false;
                              
        }

        void App_Resuming(object sender, object e)
        {
             createCamera();
             DispatcherTimerOn();
        }

        void App_Suspending(Object sender, Windows.ApplicationModel.SuspendingEventArgs e)
        {                   
            disposeCamera();
            DispatcherTimerOff();
        }

        async void disposeCamera()
        {
            
            // Release resources
            if (mediaCapture != null)
            {
                removeEffects();
                await mediaCapture.StopPreviewAsync();              
                captureElement.Source = null;
                mediaCapture.Dispose();
            }

        }

        async void createCamera()
        {
          
            CaptureUse primaryUse = CaptureUse.Video;

            mediaCapture = new MediaCapture();

            DeviceInformationCollection webcamList = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);

            DeviceInformation backWebcam = (from webcam in webcamList
                                            where webcam.EnclosureLocation != null
                                            && webcam.EnclosureLocation.Panel == Windows.Devices.Enumeration.Panel.Back
                                            select webcam).FirstOrDefault();


            await mediaCapture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                VideoDeviceId = backWebcam.Id,
                AudioDeviceId = "",
                StreamingCaptureMode = StreamingCaptureMode.Video,
                PhotoCaptureSource = PhotoCaptureSource.VideoPreview
            });

            mediaCapture.VideoDeviceController.PrimaryUse = primaryUse;

            mediaCapture.SetPreviewRotation(VideoRotation.Clockwise90Degrees);

            mediaCapture.VideoDeviceController.FlashControl.Enabled = false;

            setEffects();

            reader = new BarcodeReader
            {
                Options = new DecodingOptions
                {
                    PossibleFormats = new BarcodeFormat[] { BarcodeFormat.QR_CODE },
                    TryHarder = true
                }
            };


            captureElement.Source = mediaCapture;
            await mediaCapture.StartPreviewAsync();

            focusControl = mediaCapture.VideoDeviceController.FocusControl;

            if (focusControl.Supported == true)
            {
                FocusSettings focusSetting = new FocusSettings()
                {
                    AutoFocusRange = AutoFocusRange.FullRange,
                    Mode = FocusMode.Auto,
                    WaitForFocus = false,
                    DisableDriverFallback = false
                };

                focusControl.Configure(focusSetting);
                await mediaCapture.VideoDeviceController.ExposureControl.SetAutoAsync(true);
                //await focusControl.FocusAsync();


            }

            // zoom
            if (this.mediaCapture.VideoDeviceController.ZoomControl.Supported)
            {
                sliderZoom.Minimum = this.mediaCapture.VideoDeviceController.ZoomControl.Min;
                sliderZoom.Maximum = this.mediaCapture.VideoDeviceController.ZoomControl.Max;
                sliderZoom.StepFrequency = this.mediaCapture.VideoDeviceController.ZoomControl.Step;
            }
            
        }

        private void sliderZoom_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (this.mediaCapture.VideoDeviceController.ZoomControl.Supported)
            {
                this.mediaCapture.VideoDeviceController.ZoomControl.Value = (float)e.NewValue;
            }
        }

        
        void AnalyzeBitmap(Bitmap bitmap, TimeSpan time)
        {
            Result result = null;
            try
            {
                result = reader.Decode(
                    bitmap.Buffers[0].Buffer.ToArray(),
                    (int)bitmap.Buffers[0].Pitch,
                    (int)bitmap.Dimensions.Height,
                    BitmapFormat.Gray8
                    );
            }
            catch
            {           
                mDialog("Fehler beim Einlesen des QR-Codes", 1);
            }

            if (result != null)
            {
                var ignore = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    DispatcherTimerOff();
                    removeEffects();

                    // show frame, progress ring and text
                    enableSearchFrame();

                    // QR-Code hat nun einen json string
                    // host + andere (uid)
                    DTO.QRCodeDTO qrcDTO;
                    try
                    {
                        qrcDTO = JsonConvert.DeserializeObject<DTO.QRCodeDTO>(result.Text);
                    }
                    catch (Exception e)
                    {
                        qrcDTO = null;
                    }

                    if (qrcDTO == null)
                    {
                        disableSearchFrame();
                        mDialog("Falscher QR-Code!" + Environment.NewLine + "Weiter scannen?", 0);
                    }
                    else
                    {
                        // check http url                  
                        if (Uri.IsWellFormedUriString(qrcDTO.host, UriKind.RelativeOrAbsolute))
                        {
                            getQuestions(qrcDTO);
                        }
                        else
                        {
                            disableSearchFrame();
                            mDialog("Fehler: Keine gültige URL gefunden!", 1);
                        }
                    }
         

                });
            }
        }

        public void disableSearchFrame()
        {
            this.waitFrame.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            this.waitText.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            this.waitRing.IsActive = false;
        }

        public void enableSearchFrame()
        {
            this.waitFrame.Visibility = Windows.UI.Xaml.Visibility.Visible;
            this.waitText.Visibility = Windows.UI.Xaml.Visibility.Visible;
            this.waitRing.IsActive = true;
        }

        private async void getQuestions(DTO.QRCodeDTO aQRCDTO)
        {
            // Internet?
            if (System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
            {

                // step 1
                // Request DTO
                DTO.RequestDTO rDTO = new DTO.RequestDTO();
                rDTO.voteToken = aQRCDTO.voteToken;                

                // questions
                Helper.Functions.getDataFromServerStruct gDFS = await Helper.Functions.sendDataToServer(aQRCDTO.host, "/v1/questions", JsonConvert.SerializeObject(rDTO));

                if (gDFS.OK == false)
                {
                    var ignore = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>{ 
                        
                        disableSearchFrame();

                        DTO.ResponseDTO reDTO;
                        try
                        {
                            reDTO = JsonConvert.DeserializeObject<DTO.ResponseDTO>(gDFS.json);
                        }
                        catch (Exception e)
                        {
                            reDTO = null;
                        }

                        if (reDTO == null)
                        {
                            mDialog("Fehler: Keine Daten vom Server erhalten!", 1);
                        }
                        else
                        {
                            mDialog(Helper.Functions.serverMessage(reDTO), 1);
                        }

                        
                    
                    });
                    
                    
                }
                else
                {
                    DTO.QuestionsDTO qDTO;
                    try
                    {
                        qDTO = JsonConvert.DeserializeObject<DTO.QuestionsDTO>(gDFS.json);
                    }
                    catch (Exception e)
                    {
                        qDTO = null;
                    }
                        
                    if (qDTO == null)
                    {
                        var ignore = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => { 
                            
                            disableSearchFrame();
                            
                            mDialog("Fehler: Falsche Daten eingelesen!", 1);
                        
                        });
                        
                    }
                    // step 3
                    // navigate to the next page
                    else
                    {
                        Helper.Functions.sendDataTOCourse obj;
                        obj.qDTO = qDTO;
                        obj.qrDTO = aQRCDTO;
                        Frame.Navigate(typeof(Course), obj);
                    }

                }
            }
            else
            {
                disableSearchFrame();
                mDialog("Fehler: Keine Internetverbindung verfügbar!", 1); 
            }

        }

        

        async private void setEffects()
        {
            var definition = new LumiaAnalyzerDefinition(ColorMode.Yuv420Sp, 640, AnalyzeBitmap);

            await mediaCapture.AddEffectAsync(
                MediaStreamType.VideoPreview,
                definition.ActivatableClassId,
                definition.Properties
            );
        }

        async private void removeEffects()
        {            
            await mediaCapture.ClearEffectsAsync(
                MediaStreamType.VideoPreview              
            );
        }

        private void DispatcherTimerSetup()
        {
            dispatcherTimer = new DispatcherTimer();
            dispatcherTimer.Tick += dispatcherTimer_Tick;
            dispatcherTimer.Interval = new TimeSpan(0, 0, 1);
            dispatcherTimer.Start();
        }

        async void dispatcherTimer_Tick(object sender, object e)
        {
            try
            {
                if (focusControl.Supported == true)
                {
                    await focusControl.FocusAsync();
                }
            }
            catch
            {
                //DisposeCapture();
            }
        }

        public void DispatcherTimerOff()
        {
            dispatcherTimer.Stop();
            dispatcherTimer.Tick -= dispatcherTimer_Tick;
        }

        public void DispatcherTimerOn()
        {
            dispatcherTimer.Tick += dispatcherTimer_Tick;   
            dispatcherTimer.Start();
            
        }


        /// <summary>
        /// Wird aufgerufen, wenn diese Seite in einem Frame angezeigt werden soll.
        /// </summary>
        /// <param name="e">Ereignisdaten, die beschreiben, wie diese Seite erreicht wurde.
        /// Dieser Parameter wird normalerweise zum Konfigurieren der Seite verwendet.</param>
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {

            createCamera();
            DispatcherTimerSetup();
            HardwareButtons.BackPressed += HardwareButtons_BackPressed;

                 
        }

        protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
        {
            //HardwareButtons.BackPressed -= HardwareButtons_BackPressed;
        }

        private void HardwareButtons_BackPressed(object sender, Windows.Phone.UI.Input.BackPressedEventArgs e)
        {
            DispatcherTimerOff();
            //removeEffects();
            disposeCamera();

            Application.Current.Exit();
            //closeApp = true;
            e.Handled = true;
            //mDialog("Wollen Sie die Evaluation beenden?", 0);
           
        }





        public async void mDialog(string text, int type)
        {
            if (!OpenWindow)
            {
                MessageDialog msg = new MessageDialog(text);

                if (type == 0)
                {
                    msg.Commands.Add(new UICommand("Ja", new UICommandInvokedHandler(CommandHandlers)));
                    msg.Commands.Add(new UICommand("Nein", new UICommandInvokedHandler(CommandHandlers)));
                }
                else
                {
                    msg.Commands.Add(new UICommand("OK", new UICommandInvokedHandler(CommandHandlers)));
                }

                OpenWindow = true;

                await msg.ShowAsync();
            }
        }

        public void CommandHandlers(IUICommand commandLabel)
        {
            OpenWindow = false;
            var Actions = commandLabel.Label;
            switch (Actions)
            {
                //Ja, OK Button.
                case "Ja": case "OK":
                    if (closeApp)
                    {
                        disposeCamera();
                        Application.Current.Exit();
                    }


                    DispatcherTimerOn();
                    setEffects();

                    disableSearchFrame();               

                    break;               
                //Nein Button.
                case "Nein":
                    if (closeApp)
                    {
                        closeApp = false;
                        DispatcherTimerOn();
                        setEffects();

                        disableSearchFrame();
                    }
                    else
                    {
                        DispatcherTimerOff();
                        disposeCamera();
                        
                        // test
                        Application.Current.Exit();
                    }
                    break;
                //end.
            }
        }
  


        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            //HardwareButtons.BackPressed -= HardwareButtons_BackPressed;
            disposeCamera();
            DispatcherTimerOff();
        }

        

        

        

    }
}
