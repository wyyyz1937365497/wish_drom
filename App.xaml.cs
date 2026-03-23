using Microsoft.Maui.Platform;

namespace wish_drom
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window()
            {
                Width = 400,  // 类似手机宽度
                Height = 850, // 类似手机高度
                Title = "智能校园助手",
                Page = new MainPage()
            };
        }
    }
}
