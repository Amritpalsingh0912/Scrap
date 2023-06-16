using FluentValidation;
using ScrapWebApp.Models;

namespace ScrapWebApp.Models
{
    public class LinkModelValidator : AbstractValidator<LinkModel>
    {
        public LinkModelValidator()
        {
            //RuleFor(x => x.PartitionKey).NotEmpty();
            RuleFor(x => x.Link).NotEmpty().Must(BeAValidUrl).WithMessage("Invalid URL format");
        }

        private bool BeAValidUrl(string url)
        {
            // Implement your custom URL validation logic here
            // Return true if the URL is valid; otherwise, return false
            return Uri.TryCreate(url, UriKind.Absolute, out _);
        }
    }


}
