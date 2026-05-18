namespace SalmonEgg.Presentation.Services;

public interface ITitleBarInsetProvider
{
    (double Left, double Right, double Height) GetInsets();
}
