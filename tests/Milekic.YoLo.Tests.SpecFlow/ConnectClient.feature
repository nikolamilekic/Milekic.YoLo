Feature: ConnectClient

Background:
    Given vault with id "aaa" and title "Vault 1"
    And item with id "bbb" and title "Item 1" in vault "aaa" with fields
        | Id  | Label    | Value       |
        | ccc | username | user name 1 |
        | ddd | password | monkey123   |

Scenario: Inject in 8 different flavours
    When the user runs inject with the following text
    """
    {{ op://Vault 1/Item 1/username }}
    {{ op://aaa/Item 1/username }}
    {{ op://Vault 1/bbb/username }}
    {{ op://aaa/bbb/username }}
    {{ op://Vault 1/Item 1/password }}
    {{ op://aaa/Item 1/password }}
    {{ op://Vault 1/bbb/password }}
    {{ op://aaa/bbb/password }}
    """
    Then the result should be
    """
    user name 1
    user name 1
    user name 1
    user name 1
    monkey123
    monkey123
    monkey123
    monkey123
    """
