namespace Sepiabox.AppHost.Tests;

/// <summary>The web's optional custom domain (S-035, D-065), checked on the published Bicep and manifest.</summary>
public sealed class AzureDomainTests(PublishedInfrastructure published) : IClassFixture<PublishedInfrastructure>
{
    private const string Web = "web-containerapp.module.bicep";

    [Fact]
    public void Custom_domain_parameters_publish_no_default_so_azd_reads_them_from_its_config()
    {
        // With a published default azd uses it, or AZURE_CUSTOM_DOMAIN, and never infra.parameters (D-072);
        // scripts/deploy.sh sets the "none" values instead.
        Assert.Null(Default(AzureInfrastructure.CustomDomainParameter));
        Assert.Null(Default(AzureInfrastructure.CustomDomainBindingParameter));
        Assert.Equal("{customDomain.value}", published.ModuleParameters[Web]["customdomain_value"]!.GetValue<string>());
        Assert.Equal("{customDomainBinding.value}", published.ModuleParameters[Web]["customdomainbinding_value"]!.GetValue<string>());
    }

    [Fact]
    public void Web_has_no_custom_domain_when_the_parameter_is_empty_and_binds_the_managed_certificate_when_set()
    {
        var ingress = published.The("Microsoft.App/containerApps").Block("ingress");

        // Empty: no custom domain at all. Set: the host name, with the binding the second parameter gives,
        // and once it is SniEnabled the managed certificate domain.bicep creates in the environment.
        Assert.Contains(
            "customDomains: empty(customdomain_value) ? [] : [{ name: customdomain_value, bindingType: customdomainbinding_value, "
            + "certificateId: customdomainbinding_value == 'SniEnabled' ? "
            + "'${aca_outputs_azure_container_apps_environment_id}/managedCertificates/sepiabox-custom-domain' : null }]",
            ingress,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Managed_certificate_is_created_only_for_a_custom_domain_being_bound()
    {
        var certificate = published.The("Microsoft.App/managedEnvironments/managedCertificates");
        var parameters = published.ModuleParameters["domain.bicep"];

        Assert.Equal("!empty(customDomain) && customDomainBinding == 'SniEnabled'", certificate.Condition);
        Assert.Equal("certificateName", certificate.Property("name"));
        Assert.Equal("sepiabox-custom-domain", parameters["certificateName"]!.GetValue<string>());
        Assert.Equal("customDomain", certificate.Property("subjectName"));
        Assert.Equal("'CNAME'", certificate.Property("domainControlValidation"));
        Assert.Equal("'francecentral'", certificate.Property("location"));
        Assert.Equal("{customDomain.value}", parameters["customDomain"]!.GetValue<string>());
        Assert.Equal("{customDomainBinding.value}", parameters["customDomainBinding"]!.GetValue<string>());
        Assert.Equal("{aca.outputs.AZURE_CONTAINER_APPS_ENVIRONMENT_NAME}", parameters["environmentName"]!.GetValue<string>());
        Assert.Contains(
            "@allowed([\n  'Disabled'\n  'SniEnabled'\n])\nparam customDomainBinding string = 'Disabled'",
            published.Modules["domain.bicep"],
            StringComparison.Ordinal);
    }

    [Fact]
    public void Environment_outputs_the_custom_domain_verification_id()
    {
        // The value of the TXT record asuid.app (docs/operations.md).
        Assert.Contains(
            "output AZURE_CONTAINER_APPS_CUSTOM_DOMAIN_VERIFICATION_ID string = aca.properties.customDomainConfiguration.customDomainVerificationId",
            published.Modules["aca.module.bicep"],
            StringComparison.Ordinal);
    }

    private System.Text.Json.Nodes.JsonNode? Default(string parameter) => published.Resource(parameter)["inputs"]!["value"]!["default"];
}
